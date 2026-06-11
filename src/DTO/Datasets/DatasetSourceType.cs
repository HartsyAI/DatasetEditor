namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// Source type for datasets
/// </summary>
public enum DatasetSourceType
{
    /// <summary>Uploaded file (ZIP, CSV, Parquet, etc.)</summary>
    LocalUpload = 0,

    /// <summary>HuggingFace dataset (downloaded)</summary>
    HuggingFace = 1,

    /// <summary>Alias for HuggingFace downloaded datasets (backwards compatibility)</summary>
    HuggingFaceDownload = HuggingFace,

    /// <summary>HuggingFace dataset in streaming mode</summary>
    HuggingFaceStreaming = 2,

    /// <summary>URL to dataset file</summary>
    WebUrl = 3,

    /// <summary>Local folder on disk</summary>
    LocalFolder = 4,

    /// <summary>External S3 (or S3-compatible) streaming source</summary>
    ExternalS3Streaming = 5
}
