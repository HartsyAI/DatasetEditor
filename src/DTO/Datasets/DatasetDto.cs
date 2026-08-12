namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// General-purpose dataset DTO used by Core repository abstractions.
/// Combines the key metadata fields needed across API and services.
/// </summary>
public sealed record DatasetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IngestionStatusDto Status { get; init; } = IngestionStatusDto.Pending;
    public long TotalItems { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? SourceFileName { get; init; }
    public DatasetSourceType SourceType { get; init; } = DatasetSourceType.LocalUpload;
    public string? SourceUri { get; init; }
    public bool IsStreaming { get; init; }
    public string? HuggingFaceRepository { get; init; }
    public string? HuggingFaceConfig { get; init; }
    public string? HuggingFaceSplit { get; init; }
    /// <summary>
    /// Name of the server-side StorageProfile (env-var-configured credentials + endpoint) this
    /// S3-sourced dataset reads through. Never the credentials themselves -- same rule Phase 3
    /// established for auth secrets, extended here (see IS3StorageProfileResolver).
    /// </summary>
    public string? S3StorageProfileName { get; init; }
    public string? S3Bucket { get; init; }
    public string? S3Prefix { get; init; }
    public string? ErrorMessage { get; init; }
    public string Format { get; init; } = "Unknown";
    public string Modality { get; init; } = "Image";
    public long TotalSizeBytes { get; init; }
    public string? StoragePath { get; init; }
    public string? ParquetPath { get; init; }
    public bool IsPublic { get; init; } = true;
    public string? Metadata { get; init; }
    public Guid? CreatedByUserId { get; init; }
}
