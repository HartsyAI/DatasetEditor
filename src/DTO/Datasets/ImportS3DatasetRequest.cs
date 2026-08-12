namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// Request payload for pointing a dataset at an S3 (or S3-compatible) bucket. Never carries
/// credentials -- StorageProfileName references a server-side profile (env-var-configured),
/// and the profile's own AllowedBuckets list is the authority on which Bucket values are valid.
/// </summary>
public sealed record ImportS3DatasetRequest(
    string StorageProfileName,
    string Bucket,
    string? Prefix = null);
