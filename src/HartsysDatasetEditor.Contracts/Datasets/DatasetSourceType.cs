namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Indicates where a dataset originated from and whether it is editable locally.</summary>
public enum DatasetSourceType
{
    Unknown = 0,
    LocalUpload = 1,
    HuggingFaceDownload = 2,
    HuggingFaceStreaming = 3,
    ExternalS3Streaming = 4
}
