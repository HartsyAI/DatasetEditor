namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// A named, server-side S3 (or S3-compatible: MinIO, R2, Backblaze) connection profile, bound
/// from the <c>Storage:S3StorageProfiles</c> config array -- the same
/// <c>configuration.GetSection(...).Get&lt;T[]&gt;()</c> idiom already used for
/// <c>Cors:AllowedOrigins</c>. Real credentials belong in environment variables
/// (<c>Storage__S3StorageProfiles__0__AccessKey</c>, etc.) at deploy time, never committed to
/// appsettings.json, matching how <c>ConnectionStrings:DatasetStudio</c> and
/// <c>ADMIN_PASSWORD</c> are handled -- this class is the shape, not the secret store.
/// </summary>
public sealed class S3StorageProfileConfig
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Endpoint override for S3-compatible services (MinIO, R2, Backblaze). Null/empty means real AWS S3.</summary>
    public string? ServiceUrl { get; set; }

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Path-style addressing (bucket in the URL path, not a subdomain) -- required for MinIO
    /// and most self-hosted S3-compatible endpoints. Unset means "path style when ServiceUrl is
    /// set, virtual-hosted style for real AWS S3" (see <see cref="UsePathStyle"/>).
    /// </summary>
    public bool? ForcePathStyle { get; set; }

    /// <summary>
    /// The addressing style actually used. An explicit <see cref="ForcePathStyle"/> wins.
    /// Otherwise a custom endpoint (MinIO, R2, Backblaze) keeps the path style every profile used
    /// before, and real AWS S3 uses virtual-hosted style, which AWS requires for buckets that don't
    /// accept path-style requests; the old blanket <c>true</c> default broke those (audit S-4).
    /// </summary>
    public bool UsePathStyle => ForcePathStyle ?? !string.IsNullOrWhiteSpace(ServiceUrl);

    /// <summary>
    /// The buckets a dataset may reference through this profile. Deliberately an explicit
    /// allow-list rather than "whatever bucket the request names" -- without it, any
    /// authenticated user (registration is open) could point a dataset at any bucket these
    /// credentials can reach, turning the profile into a general-purpose credentialed read
    /// proxy. Same reasoning as ImportFromFolder's Admin-only gate and hf-thumbnail's refusal
    /// to accept a client-supplied URL.
    /// </summary>
    public List<string> AllowedBuckets { get; set; } = new();
}
