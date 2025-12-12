// TODO: Phase 3 - Extension API Client
//
// Called by: Client-side extensions to communicate with their API endpoints
// Calls: HttpClient (configured with API base URL)
//
// Purpose: Standardized HTTP client for extension API calls
// Simplifies API communication between Client and API in distributed deployments.
//
// Key Features:
// 1. Automatic URL construction based on extension ID
// 2. Typed request/response handling with JSON serialization
// 3. Error handling and logging
// 4. Authentication token management
// 5. Retry logic with exponential backoff
//
// Why This Exists:
// In distributed deployments, Client extensions need to call API extensions.
// This class provides a consistent, type-safe way to make those calls without
// manually constructing URLs or handling serialization.
//
// Usage Example (in a Client extension):
// <code>
// var client = new ExtensionApiClient(httpClient, "aitools", logger);
// var response = await client.PostAsync<CaptionRequest, CaptionResponse>(
//     "/caption",
//     new CaptionRequest { ImageUrl = "..." }
// );
// </code>
//
// Deployment Scenarios:
// - Local: Client and API on same machine (localhost)
// - Distributed: Client in browser, API on remote server
// - Cloud: Client on CDN, API on cloud provider (AWS, Azure, etc.)

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// HTTP client for making type-safe API calls from Client extensions to API extensions.
/// Handles URL construction, serialization, error handling, and logging.
/// </summary>
public class ExtensionApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _extensionId;
    private readonly ILogger? _logger;
    private readonly string _basePath;

    /// <summary>
    /// Initializes a new ExtensionApiClient.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client (with base address set)</param>
    /// <param name="extensionId">Extension identifier (e.g., "aitools")</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public ExtensionApiClient(HttpClient httpClient, string extensionId, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _extensionId = extensionId ?? throw new ArgumentNullException(nameof(extensionId));
        _logger = logger;
        _basePath = $"/api/extensions/{_extensionId}";
    }

    /// <summary>
    /// Makes a GET request to the extension API.
    /// </summary>
    /// <typeparam name="TResponse">Expected response type</typeparam>
    /// <param name="endpoint">Endpoint path (relative to extension base, e.g., "/datasets")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response or null if not found</returns>
    public async Task<TResponse?> GetAsync<TResponse>(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("GET {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return default;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error calling GET {Url}", url);
            throw new ExtensionApiException($"GET {url} failed", ex);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON deserialization error for GET {Url}", url);
            throw new ExtensionApiException($"Failed to deserialize response from {url}", ex);
        }
    }

    /// <summary>
    /// Makes a POST request to the extension API.
    /// </summary>
    /// <typeparam name="TRequest">Request body type</typeparam>
    /// <typeparam name="TResponse">Expected response type</typeparam>
    /// <param name="endpoint">Endpoint path</param>
    /// <param name="request">Request payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response</returns>
    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("POST {Url}", url);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error calling POST {Url}", url);
            throw new ExtensionApiException($"POST {url} failed", ex);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON error for POST {Url}", url);
            throw new ExtensionApiException($"Failed to process response from {url}", ex);
        }
    }

    /// <summary>
    /// Makes a POST request without expecting a response body.
    /// </summary>
    public async Task PostAsync<TRequest>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("POST {Url} (no response)", url);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error calling POST {Url}", url);
            throw new ExtensionApiException($"POST {url} failed", ex);
        }
    }

    /// <summary>
    /// Makes a PUT request to the extension API.
    /// </summary>
    public async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("PUT {Url}", url);

        try
        {
            var response = await _httpClient.PutAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error calling PUT {Url}", url);
            throw new ExtensionApiException($"PUT {url} failed", ex);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON error for PUT {Url}", url);
            throw new ExtensionApiException($"Failed to process response from {url}", ex);
        }
    }

    /// <summary>
    /// Makes a DELETE request to the extension API.
    /// </summary>
    public async Task<bool> DeleteAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("DELETE {Url}", url);

        try
        {
            var response = await _httpClient.DeleteAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error calling DELETE {Url}", url);
            throw new ExtensionApiException($"DELETE {url} failed", ex);
        }
    }

    /// <summary>
    /// Uploads a file using multipart/form-data.
    /// Useful for dataset uploads, image processing, etc.
    /// </summary>
    public async Task<TResponse?> UploadFileAsync<TResponse>(
        string endpoint,
        Stream fileStream,
        string fileName,
        Dictionary<string, string>? additionalData = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("POST (upload) {Url} - File: {FileName}", url, fileName);

        try
        {
            using var content = new MultipartFormDataContent();

            // Add file
            var fileContent = new StreamContent(fileStream);
            content.Add(fileContent, "file", fileName);

            // Add additional form data
            if (additionalData != null)
            {
                foreach (var (key, value) in additionalData)
                {
                    content.Add(new StringContent(value), key);
                }
            }

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error uploading file to {Url}", url);
            throw new ExtensionApiException($"File upload to {url} failed", ex);
        }
    }

    /// <summary>
    /// Downloads a file from the API.
    /// Returns the file content as a stream.
    /// </summary>
    public async Task<Stream?> DownloadFileAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint);
        _logger?.LogDebug("GET (download) {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error downloading from {Url}", url);
            throw new ExtensionApiException($"Download from {url} failed", ex);
        }
    }

    /// <summary>
    /// Checks if the extension API is healthy and reachable.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = BuildUrl("/health");
            var response = await _httpClient.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a full URL from an endpoint path.
    /// </summary>
    private string BuildUrl(string endpoint)
    {
        endpoint = endpoint.TrimStart('/');
        return $"{_basePath}/{endpoint}";
    }
}

/// <summary>
/// Exception thrown when an extension API call fails.
/// </summary>
public class ExtensionApiException : Exception
{
    public ExtensionApiException(string message) : base(message) { }

    public ExtensionApiException(string message, Exception innerException)
        : base(message, innerException) { }
}
