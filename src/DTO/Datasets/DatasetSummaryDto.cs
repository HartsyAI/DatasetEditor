namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// A dataset as the library lists it (<c>GET /api/datasets</c>). <see cref="DatasetDto"/>, the
/// single-dataset shape, extends it, so every field the two share is declared once with one
/// default. There used to be three near-identical records (this, DatasetDetailDto and a
/// DatasetDto shared with the API's repository) whose defaults disagreed, for example Modality
/// "" here and "Image" in the detail record (audit K-5, A-7).
/// </summary>
public record DatasetSummaryDto
{
    /// <summary>The modality every dataset has today; the app curates image datasets only.</summary>
    public const string DefaultModality = "Image";

    /// <summary>Dataset id.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Ingestion state.</summary>
    public IngestionStatusDto Status { get; init; } = IngestionStatusDto.Pending;

    /// <summary>Item count, when known.</summary>
    public long TotalItems { get; init; }

    /// <summary>When the dataset was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the dataset last changed.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Modality shown on the library card; always <see cref="DefaultModality"/> today.</summary>
    public string Modality { get; init; } = DefaultModality;

    /// <summary>Which item backend serves the dataset.</summary>
    public DatasetSourceType SourceType { get; init; } = DatasetSourceType.LocalUpload;

    /// <summary>Where the data came from (a HuggingFace URL, for example).</summary>
    public string? SourceUri { get; init; }

    /// <summary>Whether items are streamed from the source instead of stored.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>HuggingFace repository id, for HuggingFace datasets.</summary>
    public string? HuggingFaceRepository { get; init; }

    /// <summary>HuggingFace config (subset), for HuggingFace datasets.</summary>
    public string? HuggingFaceConfig { get; init; }

    /// <summary>HuggingFace split, for HuggingFace datasets.</summary>
    public string? HuggingFaceSplit { get; init; }

    /// <summary>Name of the server-side storage profile an S3 dataset reads through.</summary>
    public string? S3StorageProfileName { get; init; }

    /// <summary>Bucket, for an S3 dataset.</summary>
    public string? S3Bucket { get; init; }

    /// <summary>Key prefix, for an S3 dataset.</summary>
    public string? S3Prefix { get; init; }

    /// <summary>Whether everyone may read the dataset. New datasets are private.</summary>
    public bool IsPublic { get; init; }

    /// <summary>The owner.</summary>
    public Guid? CreatedByUserId { get; init; }
}
