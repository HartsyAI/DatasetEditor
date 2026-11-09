using System.Collections.Concurrent;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Short-TTL in-memory cache of datasets-server row pages, so requesting thumbnails for a
/// scrolled-into-view batch of grid cards (tens of individual offsets, one hf-thumbnail request
/// each) collapses to roughly one upstream GetRowsAsync call per page instead of one per card.
/// Deliberately not a general-purpose cache: keyed by (dataset, config, split, page-aligned
/// offset), values expire on a short fixed TTL rather than being invalidated, and a rare
/// concurrent-miss race can trigger a duplicate upstream fetch for the same page -- acceptable,
/// since the point is cutting request volume roughly 100x, not perfect single-flight semantics.
/// </summary>
public interface IHuggingFaceRowPageCache
{
    Task<HuggingFaceRow?> GetRowAsync(
        IHuggingFaceDatasetServerClient client,
        string dataset,
        string? config,
        string split,
        long offset,
        string? accessToken,
        CancellationToken cancellationToken);
}

public sealed class HuggingFaceRowPageCache : IHuggingFaceRowPageCache
{
    private const int PageSize = 100;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record CachedPage(DateTime ExpiresAtUtc, HuggingFaceRowsPage? Page);

    private readonly ConcurrentDictionary<string, Task<CachedPage>> _cache = new();

    public async Task<HuggingFaceRow?> GetRowAsync(
        IHuggingFaceDatasetServerClient client,
        string dataset,
        string? config,
        string split,
        long offset,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        long pageStart = offset - (offset % PageSize);
        string key = $"{dataset}|{config}|{split}|{pageStart}";

        if (!_cache.TryGetValue(key, out Task<CachedPage>? pageTask) || await IsStaleAsync(pageTask))
        {
            pageTask = FetchAsync(client, dataset, config, split, pageStart, accessToken, cancellationToken);
            _cache[key] = pageTask;
        }

        CachedPage cached = await pageTask;
        return cached.Page?.Rows.FirstOrDefault(r => r.RowIndex == offset);
    }

    private static async Task<bool> IsStaleAsync(Task<CachedPage> pageTask)
    {
        if (pageTask.IsFaulted || pageTask.IsCanceled)
        {
            return true;
        }
        CachedPage cached = await pageTask;
        return cached.ExpiresAtUtc <= DateTime.UtcNow;
    }

    private static async Task<CachedPage> FetchAsync(
        IHuggingFaceDatasetServerClient client,
        string dataset,
        string? config,
        string split,
        long pageStart,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        HuggingFaceRowsPage? page;
        try
        {
            page = await client.GetRowsAsync(
                dataset, config, split, (int)pageStart, PageSize, accessToken, cancellationToken);
        }
        catch (HuggingFaceUpstreamException)
        {
            // The client has already logged the cause. Treat it as "no page" so a thumbnail request
            // 404s rather than failing with a 500.
            page = null;
        }

        // A null page here means the lookup failed (see above). Caching that failure
        // for the full TTL would turn one transient hiccup into every thumbnail in this page's
        // 100-row window 404ing for the next 5 minutes. Cache it as already-expired instead, so
        // the very next request for this page retries rather than reusing the failure.
        DateTime expiresAtUtc = page is null ? DateTime.UtcNow : DateTime.UtcNow.Add(Ttl);
        return new CachedPage(expiresAtUtc, page);
    }
}
