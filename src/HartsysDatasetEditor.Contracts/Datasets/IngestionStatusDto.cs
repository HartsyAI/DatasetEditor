namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Represents the ingestion workflow status for a dataset.</summary>
public enum IngestionStatusDto
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
