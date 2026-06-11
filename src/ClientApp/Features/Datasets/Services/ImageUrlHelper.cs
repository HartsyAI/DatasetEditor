using DatasetStudio.ClientApp.Services.ApiClients;
using Microsoft.Extensions.Options;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Helper service for resolving image URLs to full API URLs.
/// </summary>
public sealed class ImageUrlHelper
{
    private readonly string? _apiBaseAddress;

    public ImageUrlHelper(IOptions<DatasetApiOptions> datasetApiOptions)
    {
        _apiBaseAddress = datasetApiOptions?.Value?.BaseAddress?.TrimEnd('/');
    }

    /// <summary>
    /// Converts a relative API path or absolute URL to a full URL.
    /// If the URL is relative (e.g., /api/datasets/...), prepends the API base address.
    /// If the URL is already absolute (http://...), returns it unchanged.
    /// </summary>
    /// <param name="url">The URL or path to resolve.</param>
    /// <returns>A full URL that can be used in image src attributes.</returns>
    public string ResolveImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        // If already an absolute URL (starts with http:// or https://), return as-is
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // If no API base address configured, return the path as-is (will resolve to client host)
        if (string.IsNullOrWhiteSpace(_apiBaseAddress))
        {
            return url;
        }

        // Prepend API base address to relative path
        string path = url.TrimStart('/');
        return $"{_apiBaseAddress}/{path}";
    }
}
