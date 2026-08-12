using DatasetStudio.ClientApp.Services.ApiClients;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Helper service for resolving image URLs to full API URLs.
/// </summary>
public sealed class ImageUrlHelper
{
    private readonly string _apiBaseAddress;

    public ImageUrlHelper(IOptions<DatasetApiOptions> datasetApiOptions, IWebAssemblyHostEnvironment hostEnvironment)
    {
        // Same fallback as Configuration/Program.cs's HttpClient factories: an unset
        // DatasetApi:BaseAddress means same-origin production (the API self-hosts the
        // published client), so fall back to the page's own origin instead of leaving this
        // blank. Without this fallback, WithWidth's same-origin check below could never
        // recognize our own API's URLs once BaseAddress stopped being a hardcoded dev value,
        // and thumbnails would silently request full-res originals in production.
        string? configured = datasetApiOptions?.Value?.BaseAddress?.TrimEnd('/');
        _apiBaseAddress = string.IsNullOrWhiteSpace(configured)
            ? hostEnvironment.BaseAddress.TrimEnd('/')
            : configured;
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

    /// <summary>
    /// Requests a width-bounded variant for CDNs that support it (currently Unsplash) and for
    /// our own API's thumbnail-serving routes (ServeDatasetFile's <c>?w=</c> branch and
    /// hf-thumbnail), so the grid renders small cached thumbnails instead of multi-megabyte
    /// originals. Leaves unknown (third-party, non-API) URLs untouched and never double-sizes
    /// an already-sized URL.
    /// </summary>
    /// <param name="url">Resolved image URL.</param>
    /// <param name="width">Target width in pixels.</param>
    public string WithWidth(string url, int width)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url ?? string.Empty;
        }

        if (url.Contains("w=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // Only rewrite CDNs known to honor width query params. Rewriting arbitrary
        // (possibly signed) URLs could break them, so we stay conservative.
        if (url.Contains("images.unsplash.com", StringComparison.OrdinalIgnoreCase))
        {
            char separator = url.Contains('?') ? '&' : '?';
            return $"{url}{separator}w={width}&q=80&fit=crop";
        }

        // Any URL pointing back at our own API is a route we control (ServeDatasetFile's
        // thumbnail branch, hf-thumbnail) and both honor ?w=. Compared by authority (not a
        // string prefix) so this still matches whether the URL started out relative or
        // absolute -- ResolveImageUrl above already normalized it to absolute by this point.
        if (!string.IsNullOrWhiteSpace(_apiBaseAddress)
            && Uri.TryCreate(_apiBaseAddress, UriKind.Absolute, out Uri? apiUri)
            && Uri.TryCreate(url, UriKind.Absolute, out Uri? resolvedUri)
            && string.Equals(apiUri.Authority, resolvedUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            char separator = url.Contains('?') ? '&' : '?';
            return $"{url}{separator}w={width}";
        }

        return url;
    }
}
