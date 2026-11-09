using DatasetStudio.DTO.Datasets;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Presentation;

/// <summary>
/// How a dataset's source looks everywhere in the client (DESIGN.md §5.4). Replaces the
/// library's private <c>GetSourceLabel</c>, which had no entry for server folders or URLs.
/// </summary>
/// <param name="Icon">Material icon (filled).</param>
/// <param name="Label">User-facing label.</param>
public sealed record SourcePresentation(string Icon, string Label)
{
    /// <summary>
    /// Returns the presentation for a source. A HuggingFace dataset flagged as streaming is shown
    /// as streaming even when its stored type is the download alias, which older imports used.
    /// </summary>
    /// <param name="sourceType">The stored source type.</param>
    /// <param name="isStreaming">Whether items are read from the source on demand.</param>
    public static SourcePresentation For(DatasetSourceType sourceType, bool isStreaming)
    {
        return sourceType switch
        {
            DatasetSourceType.LocalUpload => new(Icons.Material.Filled.CloudUpload, "Uploaded file"),
            DatasetSourceType.LocalFolder => new(Icons.Material.Filled.Folder, "Server folder"),
            DatasetSourceType.HuggingFaceStreaming => new(Icons.Material.Filled.Cloud, "HuggingFace (streaming)"),
            DatasetSourceType.HuggingFace when isStreaming => new(Icons.Material.Filled.Cloud, "HuggingFace (streaming)"),
            DatasetSourceType.HuggingFace => new(Icons.Material.Filled.Cloud, "HuggingFace (imported)"),
            DatasetSourceType.ExternalS3Streaming => new(Icons.Material.Filled.Storage, "S3 bucket"),
            DatasetSourceType.WebUrl => new(Icons.Material.Filled.Link, "URL"),
            _ => new(Icons.Material.Filled.HelpOutline, "Unknown source")
        };
    }
}
