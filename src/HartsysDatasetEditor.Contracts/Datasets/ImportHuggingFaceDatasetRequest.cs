namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Request payload for importing a dataset directly from the Hugging Face Hub.</summary>
public sealed record ImportHuggingFaceDatasetRequest
{
    public string Repository { get; init; } = string.Empty;
    
    public string? Revision { get; init; }
    
    public string Name { get; init; } = string.Empty;
    
    public string? Description { get; init; }
    
    public bool IsStreaming { get; init; }
    
    public string? AccessToken { get; init; }
    
    /// <summary>User-selected config (subset) for streaming mode.</summary>
    public string? Config { get; init; }
    
    /// <summary>User-selected split for streaming mode.</summary>
    public string? Split { get; init; }
    
    /// <summary>User-selected data file path for download mode.</summary>
    public string? DataFilePath { get; init; }
    
    /// <summary>User explicitly confirmed fallback to download mode when streaming failed.</summary>
    public bool ConfirmedDownloadFallback { get; init; }
}
