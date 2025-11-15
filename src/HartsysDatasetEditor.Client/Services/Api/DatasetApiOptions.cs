namespace HartsysDatasetEditor.Client.Services.Api;

/// <summary>Configuration for connecting to the Dataset API.</summary>
public sealed class DatasetApiOptions
{
    /// <summary>Base address for the API (e.g., https://localhost:7085).</summary>
    public string? BaseAddress { get; set; }
}
