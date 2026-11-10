namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Short-TTL in-memory cache of datasets-server row pages, so requesting thumbnails for a
/// scrolled-into-view batch of grid cards (tens of individual offsets, one hf-thumbnail request
/// each) collapses to roughly one upstream GetRowsAsync call per page instead of one per card.
/// Keyed by (access scope, dataset, config, split, page-aligned offset); values expire on a short
/// fixed TTL.
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

/// <summary>
/// The row-page cache: at most <see cref="DefaultMaxPages"/> pages, least recently used evicted
/// first, expired pages dropped when touched. It used to be an unbounded dictionary that never
/// removed anything, and the first caller's cancellation token was baked into the shared fetch,
/// so one cancelled request failed every other request waiting on that page (audit H-7).
/// </summary>
public sealed class HuggingFaceRowPageCache : IHuggingFaceRowPageCache
{
    /// <summary>Rows per cached page.</summary>
    public const int PageSize = 100;

    /// <summary>Pages kept by default (100 rows each).</summary>
    public const int DefaultMaxPages = 512;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A shared fetch runs on its own timeout rather than any caller's token, so a caller giving
    /// up only stops that caller waiting.
    /// </summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private sealed record CachedPage(DateTime ExpiresAtUtc, HuggingFaceRowsPage? Page);

    private readonly int _maxPages;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Task<CachedPage> Page)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Task<CachedPage> Page)> _recency = new();

    /// <summary>Creates the cache with the default size.</summary>
    public HuggingFaceRowPageCache()
        : this(DefaultMaxPages)
    {
    }

    /// <summary>Creates the cache holding at most <paramref name="maxPages"/> pages.</summary>
    public HuggingFaceRowPageCache(int maxPages)
    {
        _maxPages = Math.Max(1, maxPages);
    }

    /// <summary>Pages currently held (for tests and diagnostics).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

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
        // The access scope is part of the key: a page fetched with one user's token must not be
        // handed to a request without it (audit H-5), or its signed image URLs leak a gated repo.
        string key = $"{HuggingFaceAccessScope.For(accessToken)}|{dataset}|{config}|{split}|{pageStart}";

        Task<CachedPage> pageTask;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<(string Key, Task<CachedPage> Page)>? node) && !IsStale(node.Value.Page))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                pageTask = node.Value.Page;
            }
            else
            {
                if (node is not null)
                {
                    _recency.Remove(node);
                    _entries.Remove(key);
                }

                pageTask = FetchAsync(client, dataset, config, split, pageStart, accessToken);
                _entries[key] = _recency.AddFirst((key, pageTask));
                while (_entries.Count > _maxPages)
                {
                    LinkedListNode<(string Key, Task<CachedPage> Page)> oldest = _recency.Last!;
                    _recency.RemoveLast();
                    _entries.Remove(oldest.Value.Key);
                }
            }
        }

        CachedPage cached = await pageTask.WaitAsync(cancellationToken);
        return cached.Page?.Rows.FirstOrDefault(r => r.RowIndex == offset);
    }

    /// <summary>A failed, cancelled or expired fetch is replaced on the next request; one in flight is shared.</summary>
    private static bool IsStale(Task<CachedPage> pageTask)
    {
        if (pageTask.IsFaulted || pageTask.IsCanceled)
        {
            return true;
        }
        return pageTask.IsCompletedSuccessfully && pageTask.Result.ExpiresAtUtc <= DateTime.UtcNow;
    }

    private static async Task<CachedPage> FetchAsync(
        IHuggingFaceDatasetServerClient client,
        string dataset,
        string? config,
        string split,
        long pageStart,
        string? accessToken)
    {
        HuggingFaceRowsPage? page;
        using CancellationTokenSource timeout = new(FetchTimeout);
        try
        {
            page = await client.GetRowsAsync(
                dataset, config, split, (int)pageStart, PageSize, accessToken, timeout.Token);
        }
        catch (HuggingFaceUpstreamException)
        {
            // The client has already logged the cause. Treat it as "no page" so a thumbnail request
            // 404s rather than failing with a 500.
            page = null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
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
