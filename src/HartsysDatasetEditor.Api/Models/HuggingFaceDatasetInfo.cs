namespace HartsysDatasetEditor.Api.Models;

/// <summary>
/// Metadata about a HuggingFace dataset.
/// </summary>
public sealed record HuggingFaceDatasetInfo
{
    public string Id { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Sha { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public bool Private { get; init; }
    public bool Gated { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<HuggingFaceDatasetFile> Files { get; init; } = new();
}

/// <summary>
/// Represents a file in a HuggingFace dataset repository.
/// </summary>
public sealed record HuggingFaceDatasetFile
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Type { get; init; } = string.Empty;
}
