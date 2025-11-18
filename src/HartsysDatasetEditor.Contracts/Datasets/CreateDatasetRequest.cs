namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Request payload for creating a new dataset definition.</summary>
public sealed record CreateDatasetRequest(
    string Name,
    string? Description,
    DatasetSourceType SourceType = DatasetSourceType.LocalUpload,
    string? SourceUri = null,
    bool IsStreaming = false);
