using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// A dataset row as the API works with it: what <see cref="IDatasetRepository"/> reads and
/// writes, and what permission checks and ingestion take. It is not a wire type. It used to be
/// <c>DTO.Datasets.DatasetDto</c>, shared with the client only because Core's repository
/// interfaces took it, although it carries <see cref="StoragePath"/>, a server folder path the
/// client must not see (audit K-3, K-5). Responses map it to <see cref="DatasetDto"/> or
/// <see cref="DatasetSummaryDto"/> in <c>DatasetMappings</c>.
/// </summary>
public sealed record DatasetRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Ingestion state.</summary>
    public IngestionStatusDto Status { get; init; } = IngestionStatusDto.Pending;

    /// <summary>Item count, as last written by ingestion or learned from the source.</summary>
    public long TotalItems { get; init; }

    /// <summary>When the row was created (stamped by the database context).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the row last changed (stamped by the database context).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>The uploaded file's name, for an upload.</summary>
    public string? SourceFileName { get; init; }

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

    /// <summary>
    /// Name of the server-side storage profile an S3 dataset reads through. Never the credentials
    /// themselves (see IS3StorageProfileResolver).
    /// </summary>
    public string? S3StorageProfileName { get; init; }

    /// <summary>Bucket, for an S3 dataset.</summary>
    public string? S3Bucket { get; init; }

    /// <summary>Key prefix, for an S3 dataset.</summary>
    public string? S3Prefix { get; init; }

    /// <summary>Why ingestion failed, when it did.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Always "Image" today; see DatasetEntity.Modality.</summary>
    public string Modality { get; init; } = DatasetSummaryDto.DefaultModality;

    /// <summary>Server folder of a folder import or extracted upload. Never sent to clients.</summary>
    public string? StoragePath { get; init; }

    /// <summary>Whether everyone may read the dataset.</summary>
    public bool IsPublic { get; init; }

    /// <summary>The owner.</summary>
    public Guid? CreatedByUserId { get; init; }
}
