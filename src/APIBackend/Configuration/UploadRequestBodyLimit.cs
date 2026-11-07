using System.Text.RegularExpressions;

namespace DatasetStudio.APIBackend.Configuration;

/// <summary>
/// Request-body limits for the API. Every endpoint gets <see cref="DefaultMaxRequestBodyBytes"/>;
/// only the dataset upload route gets <see cref="MaxUploadBodyBytes"/>, and Program.cs raises the
/// limit for that route before the body is read. Before this split, every endpoint accepted 5 GB
/// bodies (audit C-6).
/// </summary>
internal static class UploadRequestBodyLimit
{
    /// <summary>Largest request body any endpoint accepts unless it is the upload route.</summary>
    internal const long DefaultMaxRequestBodyBytes = 10L * 1024 * 1024;

    /// <summary>Largest body the dataset upload route accepts.</summary>
    internal const long MaxUploadBodyBytes = 5L * 1024 * 1024 * 1024;

    // The route captures the dataset segment and it is validated with Guid.TryParse below, the same
    // parser the {datasetId:guid} route constraint uses. A fixed-length pattern would miss the N, B
    // and P GUID formats, which the route accepts too.
    private static readonly Regex DatasetUploadRoute = new(
        @"^/api/datasets/([^/]+)/upload/?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns true when <paramref name="path"/> is the upload route for a dataset, so it receives
    /// <see cref="MaxUploadBodyBytes"/>.
    /// </summary>
    /// <param name="path">The request path, already URL-decoded (HttpRequest.Path.Value).</param>
    /// <returns>True for /api/datasets/{guid}/upload with any GUID format the route accepts.</returns>
    internal static bool IsDatasetUploadPath(string path)
    {
        Match match = DatasetUploadRoute.Match(path);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out _);
    }
}
