using System.Text.Json;
using System.Text.Json.Serialization;
using DatasetStudio.APIBackend.Models;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Implementation of HuggingFace Hub API client.
/// API docs: https://huggingface.co/docs/hub/api
/// </summary>
internal sealed class HuggingFaceClient : IHuggingFaceClient
{
    private const string HuggingFaceApiBase = "https://huggingface.co";
    private const string HuggingFaceCdnBase = "https://cdn-lfs.huggingface.co";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HuggingFaceClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public HuggingFaceClient(HttpClient httpClient, ILogger<HuggingFaceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<HuggingFaceDatasetInfo?> GetDatasetInfoAsync(
        string repository,
        string? revision = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            revision ??= "main";
            string url = $"{HuggingFaceApiBase}/api/datasets/{EscapePath(repository)}";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch HuggingFace dataset info for {Repository}: {StatusCode}",
                    repository, response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            HuggingFaceApiResponse? apiResponse = JsonSerializer.Deserialize<HuggingFaceApiResponse>(json, _jsonOptions);

            if (apiResponse == null)
            {
                return null;
            }

            // Fetch file tree to get dataset files
            List<HuggingFaceDatasetFile> files = await GetDatasetFilesAsync(repository, revision, accessToken, cancellationToken);

            return new HuggingFaceDatasetInfo
            {
                Id = apiResponse.Id ?? repository,
                Author = apiResponse.Author ?? string.Empty,
                Sha = apiResponse.Sha ?? string.Empty,
                LastModified = apiResponse.LastModified,
                Private = apiResponse.Private,
                Gated = apiResponse.Gated is { ValueKind: JsonValueKind.True or JsonValueKind.String },
                Tags = apiResponse.Tags ?? new List<string>(),
                Files = files
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching HuggingFace dataset info for {Repository}", repository);
            return null;
        }
    }

    private async Task<List<HuggingFaceDatasetFile>> GetDatasetFilesAsync(
        string repository,
        string revision,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            // recursive=true: without it the Hub lists only the top level, so the usual layout
            // (data/train-00000-of-00001.parquet) looked like a repository with no data files
            // (audit I-7). Large repositories are paged; the next page is in the Link header.
            string? url = $"{HuggingFaceApiBase}/api/datasets/{EscapePath(repository)}/tree/{Uri.EscapeDataString(revision)}?recursive=true";
            List<HuggingFaceDatasetFile> files = new();

            for (int page = 0; url is not null && page < MaxTreePages; page++)
            {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                }

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch file tree for {Repository}: {StatusCode}", repository, response.StatusCode);
                    break;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                List<HuggingFaceFileTreeItem>? items = JsonSerializer.Deserialize<List<HuggingFaceFileTreeItem>>(json, _jsonOptions);
                files.AddRange((items ?? new List<HuggingFaceFileTreeItem>())
                    .Where(f => f.Type == "file")
                    .Select(f => new HuggingFaceDatasetFile
                    {
                        Path = f.Path ?? string.Empty,
                        Size = f.Size,
                        Type = GetFileType(f.Path)
                    }));

                url = GetNextPageUrl(response);
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching file tree for {Repository}", repository);
            return new List<HuggingFaceDatasetFile>();
        }
    }

    public async Task DownloadFileAsync(
        string repository,
        string fileName,
        string destinationPath,
        string? revision = null,
        string? accessToken = null,
        long? maxBytes = null,
        CancellationToken cancellationToken = default)
    {
        revision ??= "main";

        // Files usually sit in folders (data/train-00000.parquet), and a raw name with spaces or
        // '#' broke the URL; EscapePath keeps the folder slashes.
        string url = $"{HuggingFaceApiBase}/datasets/{EscapePath(repository)}/resolve/{Uri.EscapeDataString(revision)}/{EscapePath(fileName)}";

        _logger.LogInformation("Downloading {FileName} from {Repository} to {Destination}",
            fileName, repository, destinationPath);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        if (maxBytes is long limit && totalBytes > limit)
        {
            throw new InvalidOperationException(DownloadTooLargeMessage(fileName, limit));
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool completed = false;
        try
        {
            await CopyWithProgressAsync(response, destinationPath, fileName, totalBytes, maxBytes, cancellationToken);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                TryDeletePartialDownload(destinationPath);
            }
        }
    }

    /// <summary>
    /// Escapes each <c>/</c>-separated segment of a repository id (<c>owner/name</c>) or file path
    /// and keeps the slashes. The repository used to be interpolated raw, so a value containing
    /// <c>?</c>, <c>#</c> or <c>%</c> changed which URL was requested (audit H-3). A <c>.</c> or
    /// <c>..</c> segment is refused: System.Uri collapses it, even percent-encoded, into a
    /// different Hub path (<c>../../models/x</c> requested <c>/models/x</c>).
    /// </summary>
    internal static string EscapePath(string path)
    {
        return string.Join('/', path.Split('/').Select(EscapeSegment));
    }

    private static string EscapeSegment(string segment)
    {
        if (segment is "." or "..")
        {
            throw new ArgumentException($"'{segment}' is not allowed as a path segment in a HuggingFace repository or file path.");
        }
        return Uri.EscapeDataString(segment);
    }

    /// <summary>Most tree pages followed for one repository (the Hub returns 1,000 entries per page).</summary>
    private const int MaxTreePages = 50;

    /// <summary>The message for a file over the download limit.</summary>
    internal static string DownloadTooLargeMessage(string fileName, long maxBytes)
    {
        return $"{fileName} is larger than the {maxBytes / (1024.0 * 1024 * 1024):0.##} GB download limit (Ingestion:MaxDownloadBytes).";
    }

    /// <summary>The <c>rel="next"</c> URL from a Hub Link header, or null on the last page.</summary>
    private static string? GetNextPageUrl(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string part in values.SelectMany(v => v.Split(',')))
        {
            string trimmed = part.Trim();
            if (trimmed.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
            {
                int start = trimmed.IndexOf('<');
                int end = trimmed.IndexOf('>');
                if (start >= 0 && end > start)
                {
                    return trimmed[(start + 1)..end];
                }
            }
        }
        return null;
    }

    private void TryDeletePartialDownload(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete partial download {Path}", path);
        }
    }

    private async Task CopyWithProgressAsync(
        HttpResponseMessage response,
        string destinationPath,
        string fileName,
        long? totalBytes,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192);
        using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Download with progress reporting
        byte[] buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;
        long lastLoggedBytes = 0;
        long logInterval = totalBytes.HasValue ? Math.Max(1024 * 1024 * 100, totalBytes.Value / 20) : 1024 * 1024 * 100; // Log every 100MB or 5%
        DateTime lastLogTime = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalBytesRead += bytesRead;
            // A missing or wrong Content-Length must not let a download run past the limit.
            if (maxBytes is long limit && totalBytesRead > limit)
            {
                throw new InvalidOperationException(DownloadTooLargeMessage(fileName, limit));
            }
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            // Log progress periodically
            if (totalBytesRead - lastLoggedBytes >= logInterval || (DateTime.UtcNow - lastLogTime).TotalSeconds >= 5)
            {
                if (totalBytes.HasValue)
                {
                    double percentComplete = (totalBytesRead * 100.0) / totalBytes.Value;
                    double downloadedGB = totalBytesRead / (1024.0 * 1024.0 * 1024.0);
                    double totalGB = totalBytes.Value / (1024.0 * 1024.0 * 1024.0);
                    _logger.LogInformation("Download progress: {Percent:F1}% ({DownloadedGB:F2} GB / {TotalGB:F2} GB)",
                        percentComplete, downloadedGB, totalGB);
                }
                else
                {
                    double downloadedMB = totalBytesRead / (1024.0 * 1024.0);
                    _logger.LogInformation("Download progress: {DownloadedMB:F2} MB downloaded",
                        downloadedMB);
                }

                lastLoggedBytes = totalBytesRead;
                lastLogTime = DateTime.UtcNow;
            }
        }

        _logger.LogInformation("Downloaded {FileName} ({Size} bytes) to {Destination}",
            fileName, totalBytesRead, destinationPath);
    }

    private static string GetFileType(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "unknown";
        }

        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "parquet" => "parquet",
            "csv" => "csv",
            "json" or "jsonl" => "json",
            "arrow" => "arrow",
            _ => extension
        };
    }

    // Internal DTOs for HuggingFace API responses
    private sealed class HuggingFaceApiResponse
    {
        [JsonPropertyName("_id")]
        public string? Id { get; set; }

        public string? Author { get; set; }
        public string? Sha { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        public bool Private { get; set; }

        /// <summary>
        /// <c>false</c>, or <c>"auto"</c>/<c>"manual"</c> for a gated repository. Typed as bool?, the
        /// string form failed deserialization and every gated dataset looked "not found".
        /// </summary>
        public JsonElement? Gated { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed class HuggingFaceFileTreeItem
    {
        public string? Path { get; set; }
        public string? Type { get; set; }
        public long Size { get; set; }
    }
}

