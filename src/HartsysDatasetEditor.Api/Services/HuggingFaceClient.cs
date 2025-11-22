using System.Text.Json;
using System.Text.Json.Serialization;
using HartsysDatasetEditor.Api.Models;

namespace HartsysDatasetEditor.Api.Services;

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
            string url = $"{HuggingFaceApiBase}/api/datasets/{repository}";

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
                Gated = apiResponse.Gated.GetValueOrDefault(),
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
            // HuggingFace API endpoint for file tree
            string url = $"{HuggingFaceApiBase}/api/datasets/{repository}/tree/{revision}";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch file tree for {Repository}", repository);
                return new List<HuggingFaceDatasetFile>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            List<HuggingFaceFileTreeItem>? items = JsonSerializer.Deserialize<List<HuggingFaceFileTreeItem>>(json, _jsonOptions);

            if (items == null)
            {
                return new List<HuggingFaceDatasetFile>();
            }

            return items
                .Where(f => f.Type == "file")
                .Select(f => new HuggingFaceDatasetFile
                {
                    Path = f.Path ?? string.Empty,
                    Size = f.Size,
                    Type = GetFileType(f.Path)
                })
                .ToList();
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
        CancellationToken cancellationToken = default)
    {
        revision ??= "main";

        // HuggingFace file download URL format
        string url = $"{HuggingFaceApiBase}/datasets/{repository}/resolve/{revision}/{fileName}";

        _logger.LogInformation("Downloading {FileName} from {Repository} to {Destination}",
            fileName, repository, destinationPath);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Downloaded {FileName} ({Size} bytes) to {Destination}",
            fileName, fileStream.Length, destinationPath);
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
        public bool? Gated { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed class HuggingFaceFileTreeItem
    {
        public string? Path { get; set; }
        public string? Type { get; set; }
        public long Size { get; set; }
    }
}
