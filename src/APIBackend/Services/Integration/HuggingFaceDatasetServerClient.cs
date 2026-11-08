using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Client for the Hugging Face datasets-server API used for streaming dataset metadata and rows.
/// Docs: https://huggingface.co/docs/dataset-viewer
/// </summary>
public interface IHuggingFaceDatasetServerClient
{
    Task<HuggingFaceDatasetSizeInfo?> GetDatasetSizeAsync(
        string dataset,
        string? config,
        string? split,
        string? accessToken,
        CancellationToken cancellationToken = default);

    Task<List<HuggingFaceDatasetSplitInfo>?> GetAllSplitsAsync(
        string dataset,
        string? accessToken,
        CancellationToken cancellationToken = default);

    Task<HuggingFaceRowsPage?> GetRowsAsync(
        string dataset,
        string? config,
        string split,
        int offset,
        int length,
        string? accessToken,
        CancellationToken cancellationToken = default);
}

internal sealed class HuggingFaceDatasetServerClient : IHuggingFaceDatasetServerClient
{
    private const string DatasetServerBaseUrl = "https://datasets-server.huggingface.co";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HuggingFaceDatasetServerClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public HuggingFaceDatasetServerClient(HttpClient httpClient, ILogger<HuggingFaceDatasetServerClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Reads <c>/size</c> for one config/split. The row count is that split's own count (or the
    /// whole dataset's total only when datasets-server returns no splits at all). Returns null when
    /// the response has no size section. Upstream failures throw <see cref="HuggingFaceUpstreamException"/>.
    /// </summary>
    public async Task<HuggingFaceDatasetSizeInfo?> GetDatasetSizeAsync(
        string dataset,
        string? config,
        string? split,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataset))
        {
            throw new ArgumentException("Dataset name is required", nameof(dataset));
        }

        string url = DatasetServerBaseUrl + "/size?dataset=" + Uri.EscapeDataString(dataset);

        if (!string.IsNullOrWhiteSpace(config))
        {
            url += "&config=" + Uri.EscapeDataString(config);
        }

        if (!string.IsNullOrWhiteSpace(split))
        {
            url += "&split=" + Uri.EscapeDataString(split);
        }

        string json = await GetJsonAsync(url, accessToken, "/size", dataset, cancellationToken).ConfigureAwait(false);
        HfSizeResponse? parsed = ParseBody<HfSizeResponse>(json, "/size", dataset);

        if (parsed?.Size == null)
        {
            return null;
        }

        bool hasSplits = parsed.Size.Splits is { Count: > 0 };
        HfSizeSplitEntry? chosenSplit = hasSplits ? SelectSplit(parsed.Size.Splits, config, split) : null;

        // With splits present, the count is the selected split's own count. Using the dataset-wide
        // total here made every config/split report the sum over all of them (audit H-1(4)).
        long? totalRows = hasSplits ? chosenSplit?.NumRows : parsed.Size.Dataset?.NumRows;

        return new HuggingFaceDatasetSizeInfo
        {
            Dataset = dataset,
            Config = string.IsNullOrWhiteSpace(config) ? chosenSplit?.Config : config,
            Split = string.IsNullOrWhiteSpace(split) ? chosenSplit?.Split : split,
            NumRows = totalRows,
        };
    }

    /// <summary>Lists every config/split datasets-server knows about. Returns null when there are none.</summary>
    public async Task<List<HuggingFaceDatasetSplitInfo>?> GetAllSplitsAsync(
        string dataset,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataset))
        {
            throw new ArgumentException("Dataset name is required", nameof(dataset));
        }

        string url = DatasetServerBaseUrl + "/size?dataset=" + Uri.EscapeDataString(dataset);
        string json = await GetJsonAsync(url, accessToken, "/size", dataset, cancellationToken).ConfigureAwait(false);
        HfSizeResponse? parsed = ParseBody<HfSizeResponse>(json, "/size", dataset);

        if (parsed?.Size?.Splits == null || parsed.Size.Splits.Count == 0)
        {
            return null;
        }

        List<HuggingFaceDatasetSplitInfo> splits = new List<HuggingFaceDatasetSplitInfo>(parsed.Size.Splits.Count);
        foreach (HfSizeSplitEntry splitEntry in parsed.Size.Splits)
        {
            splits.Add(new HuggingFaceDatasetSplitInfo
            {
                Dataset = splitEntry.Dataset,
                Config = splitEntry.Config,
                Split = splitEntry.Split,
                NumRows = splitEntry.NumRows
            });
        }

        return splits;
    }

    /// <summary>
    /// Reads one page of rows. Upstream failures (including 4xx/5xx) and unreadable bodies throw
    /// <see cref="HuggingFaceUpstreamException"/> rather than returning null, so an empty page is
    /// never mistaken for an empty dataset (audit H-6).
    /// </summary>
    public async Task<HuggingFaceRowsPage?> GetRowsAsync(
        string dataset,
        string? config,
        string split,
        int offset,
        int length,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataset))
        {
            throw new ArgumentException("Dataset name is required", nameof(dataset));
        }

        if (string.IsNullOrWhiteSpace(split))
        {
            throw new ArgumentException("Split is required", nameof(split));
        }

        if (offset < 0)
        {
            offset = 0;
        }

        if (length <= 0)
        {
            length = 100;
        }

        string url = DatasetServerBaseUrl + "/rows?dataset=" + Uri.EscapeDataString(dataset) +
                     "&split=" + Uri.EscapeDataString(split) +
                     "&offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                     "&length=" + length.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(config))
        {
            url += "&config=" + Uri.EscapeDataString(config);
        }

        string json = await GetJsonAsync(url, accessToken, "/rows", dataset, cancellationToken).ConfigureAwait(false);
        HfRowsResponse? parsed = ParseBody<HfRowsResponse>(json, "/rows", dataset);

        if (parsed?.Rows == null)
        {
            throw new HuggingFaceUpstreamException($"datasets-server /rows returned no rows section for {dataset}");
        }

        List<HuggingFaceRow> rows = new List<HuggingFaceRow>(parsed.Rows.Count);

        foreach (HfRowsResponseRow sourceRow in parsed.Rows)
        {
            if (sourceRow.Row == null)
            {
                continue;
            }

            rows.Add(new HuggingFaceRow
            {
                RowIndex = sourceRow.RowIndex,
                Columns = sourceRow.Row
            });
        }

        return new HuggingFaceRowsPage
        {
            Dataset = dataset,
            Config = config,
            Split = split,
            NumRowsTotal = parsed.NumRowsTotal,
            Rows = rows
        };
    }

    /// <summary>
    /// Sends a GET and returns the body. A non-success status, a transport failure or a timeout
    /// (that was not caused by the caller's cancellation) throws <see cref="HuggingFaceUpstreamException"/>.
    /// </summary>
    private async Task<string> GetJsonAsync(
        string url,
        string? accessToken,
        string endpoint,
        string dataset,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[HF DATASETS-SERVER] {Endpoint} failed for {Dataset}: {StatusCode}", endpoint, dataset, response.StatusCode);
                throw new HuggingFaceUpstreamException(
                    $"datasets-server {endpoint} returned {(int)response.StatusCode} for {dataset}",
                    response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[HF DATASETS-SERVER] {Endpoint} unreachable for {Dataset}", endpoint, dataset);
            throw new HuggingFaceUpstreamException($"datasets-server {endpoint} was unreachable for {dataset}", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[HF DATASETS-SERVER] {Endpoint} timed out for {Dataset}", endpoint, dataset);
            throw new HuggingFaceUpstreamException($"datasets-server {endpoint} timed out for {dataset}", inner: ex);
        }
    }

    /// <summary>Deserializes a datasets-server body. A body that is not JSON is an upstream fault, not an empty result.</summary>
    private T? ParseBody<T>(string json, string endpoint, string dataset) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[HF DATASETS-SERVER] {Endpoint} returned an unreadable body for {Dataset}", endpoint, dataset);
            throw new HuggingFaceUpstreamException($"datasets-server {endpoint} returned an unreadable body for {dataset}", inner: ex);
        }
    }

    /// <summary>
    /// Picks the /size entry for the requested config/split. With an explicit split the match must
    /// be exact within the requested config. Otherwise the default is the first "train" split (of
    /// the requested config when one was given), falling back to the first entry.
    /// </summary>
    private static HfSizeSplitEntry? SelectSplit(List<HfSizeSplitEntry> splits, string? config, string? split)
    {
        IEnumerable<HfSizeSplitEntry> candidates = string.IsNullOrWhiteSpace(config)
            ? splits
            : splits.Where(s => string.Equals(s.Config, config, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(split))
        {
            return candidates.FirstOrDefault(s => string.Equals(s.Split, split, StringComparison.Ordinal));
        }

        return candidates.FirstOrDefault(s => string.Equals(s.Split, "train", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private sealed class HfSizeResponse
    {
        [JsonPropertyName("size")]
        public HfSizeSection? Size { get; set; }
    }

    private sealed class HfSizeSection
    {
        [JsonPropertyName("dataset")]
        public HfSizeDatasetEntry? Dataset { get; set; }

        [JsonPropertyName("splits")]
        public List<HfSizeSplitEntry> Splits { get; set; } = new List<HfSizeSplitEntry>();
    }

    private sealed class HfSizeDatasetEntry
    {
        [JsonPropertyName("num_rows")]
        public long NumRows { get; set; }
    }

    private sealed class HfSizeSplitEntry
    {
        [JsonPropertyName("dataset")]
        public string Dataset { get; set; } = string.Empty;

        [JsonPropertyName("config")]
        public string Config { get; set; } = string.Empty;

        [JsonPropertyName("split")]
        public string Split { get; set; } = string.Empty;

        [JsonPropertyName("num_rows")]
        public long NumRows { get; set; }
    }

    private sealed class HfRowsResponse
    {
        [JsonPropertyName("rows")]
        public List<HfRowsResponseRow>? Rows { get; set; }

        [JsonPropertyName("num_rows_total")]
        public long NumRowsTotal { get; set; }
    }

    private sealed class HfRowsResponseRow
    {
        [JsonPropertyName("row_idx")]
        public long RowIndex { get; set; }

        [JsonPropertyName("row")]
        public Dictionary<string, JsonElement>? Row { get; set; }
    }
}

/// <summary>
/// Summary information about a dataset's size and default config/split as reported by datasets-server.
/// </summary>
public sealed class HuggingFaceDatasetSizeInfo
{
    public string Dataset { get; set; } = string.Empty;

    public string? Config { get; set; }

    public string? Split { get; set; }

    public long? NumRows { get; set; }
}

/// <summary>
/// A page of rows streamed from datasets-server.
/// </summary>
public sealed class HuggingFaceRowsPage
{
    public string Dataset { get; set; } = string.Empty;

    public string? Config { get; set; }

    public string Split { get; set; } = string.Empty;

    public long NumRowsTotal { get; set; }

    public List<HuggingFaceRow> Rows { get; set; } = new List<HuggingFaceRow>();
}

public sealed class HuggingFaceRow
{
    public long RowIndex { get; set; }

    public Dictionary<string, JsonElement> Columns { get; set; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Information about a specific config/split combination.
/// </summary>
public sealed class HuggingFaceDatasetSplitInfo
{
    public string Dataset { get; set; } = string.Empty;
    public string? Config { get; set; }
    public string Split { get; set; } = string.Empty;
    public long NumRows { get; set; }
}

