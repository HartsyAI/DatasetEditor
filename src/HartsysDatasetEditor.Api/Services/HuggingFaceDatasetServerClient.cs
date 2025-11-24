using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// Client for the Hugging Face datasets-server API used for streaming dataset metadata and rows.
/// Docs: https://huggingface.co/docs/dataset-viewer
/// </summary>
internal interface IHuggingFaceDatasetServerClient
{
    Task<HuggingFaceDatasetSizeInfo?> GetDatasetSizeAsync(
        string dataset,
        string? config,
        string? split,
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

    public async Task<HuggingFaceDatasetSizeInfo?> GetDatasetSizeAsync(
        string dataset,
        string? config,
        string? split,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        try
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

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[HF DATASETS-SERVER] /size failed for {Dataset}: {StatusCode}", dataset, response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            HfSizeResponse? parsed = JsonSerializer.Deserialize<HfSizeResponse>(json, _jsonOptions);

            if (parsed == null || parsed.Size == null)
            {
                return null;
            }

            string? selectedConfig = config;
            string? selectedSplit = split;
            long? totalRows = null;

            if (parsed.Size.Dataset != null)
            {
                totalRows = parsed.Size.Dataset.NumRows;
            }

            if (parsed.Size.Splits != null && parsed.Size.Splits.Count > 0)
            {
                HfSizeSplitEntry? chosenSplit = null;

                foreach (HfSizeSplitEntry splitEntry in parsed.Size.Splits)
                {
                    if (string.Equals(splitEntry.Split, "train", StringComparison.OrdinalIgnoreCase))
                    {
                        chosenSplit = splitEntry;
                        break;
                    }
                }

                if (chosenSplit == null)
                {
                    chosenSplit = parsed.Size.Splits[0];
                }

                if (string.IsNullOrWhiteSpace(selectedConfig))
                {
                    selectedConfig = chosenSplit.Config;
                }

                if (string.IsNullOrWhiteSpace(selectedSplit))
                {
                    selectedSplit = chosenSplit.Split;
                }

                if (!totalRows.HasValue)
                {
                    long sum = 0;

                    foreach (HfSizeSplitEntry splitEntry in parsed.Size.Splits)
                    {
                        sum += splitEntry.NumRows;
                    }

                    totalRows = sum;
                }
            }

            HuggingFaceDatasetSizeInfo result = new HuggingFaceDatasetSizeInfo
            {
                Dataset = dataset,
                Config = selectedConfig,
                Split = selectedSplit,
                NumRows = totalRows
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HF DATASETS-SERVER] Error calling /size for {Dataset}", dataset);
            return null;
        }
    }

    public async Task<HuggingFaceRowsPage?> GetRowsAsync(
        string dataset,
        string? config,
        string split,
        int offset,
        int length,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        try
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

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[HF DATASETS-SERVER] /rows failed for {Dataset}: {StatusCode}", dataset, response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            HfRowsResponse? parsed = JsonSerializer.Deserialize<HfRowsResponse>(json, _jsonOptions);

            if (parsed == null || parsed.Rows == null)
            {
                return null;
            }

            List<HuggingFaceRow> rows = new List<HuggingFaceRow>(parsed.Rows.Count);

            foreach (HfRowsResponseRow sourceRow in parsed.Rows)
            {
                if (sourceRow.Row == null)
                {
                    continue;
                }

                HuggingFaceRow mapped = new HuggingFaceRow
                {
                    RowIndex = sourceRow.RowIndex,
                    Columns = sourceRow.Row
                };

                rows.Add(mapped);
            }

            HuggingFaceRowsPage page = new HuggingFaceRowsPage
            {
                Dataset = dataset,
                Config = config,
                Split = split,
                NumRowsTotal = parsed.NumRowsTotal,
                Rows = rows
            };

            return page;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HF DATASETS-SERVER] Error calling /rows for {Dataset}", dataset);
            return null;
        }
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
internal sealed class HuggingFaceDatasetSizeInfo
{
    public string Dataset { get; set; } = string.Empty;

    public string? Config { get; set; }

    public string? Split { get; set; }

    public long? NumRows { get; set; }
}

/// <summary>
/// A page of rows streamed from datasets-server.
/// </summary>
internal sealed class HuggingFaceRowsPage
{
    public string Dataset { get; set; } = string.Empty;

    public string? Config { get; set; }

    public string Split { get; set; } = string.Empty;

    public long NumRowsTotal { get; set; }

    public List<HuggingFaceRow> Rows { get; set; } = new List<HuggingFaceRow>();
}

internal sealed class HuggingFaceRow
{
    public long RowIndex { get; set; }

    public Dictionary<string, JsonElement> Columns { get; set; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}
