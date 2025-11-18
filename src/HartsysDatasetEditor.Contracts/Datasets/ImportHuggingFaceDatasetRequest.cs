namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Request payload for importing a dataset directly from the Hugging Face Hub.</summary>
public sealed record ImportHuggingFaceDatasetRequest
(
    string Repository,
    string? Revision,
    string Name,
    string? Description,
    bool IsStreaming,
    string? AccessToken
);
