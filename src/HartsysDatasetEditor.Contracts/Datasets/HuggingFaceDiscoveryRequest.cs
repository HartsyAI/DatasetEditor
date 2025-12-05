namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>
/// Request to discover available configs/splits/files for a HuggingFace dataset.
/// </summary>
public sealed record HuggingFaceDiscoveryRequest
{
    public string Repository { get; init; } = string.Empty;
    
    public string? Revision { get; init; }
    
    public bool IsStreaming { get; init; }
    
    public string? AccessToken { get; init; }
}
