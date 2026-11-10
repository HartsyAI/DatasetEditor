namespace DatasetStudio.APIBackend.Services.Storage;

public sealed record S3ListPage(IReadOnlyList<string> Keys, string? NextContinuationToken);

/// <summary>Thin wrapper over the AWS S3 SDK, scoped to a named, pre-configured storage profile.</summary>
public interface IS3StorageService
{
    /// <summary>
    /// Opens an object for reading. Returns null if the object doesn't exist (S3 404), rather than
    /// throwing. The caller owns the result and must dispose it.
    /// </summary>
    Task<S3ObjectContent?> GetObjectAsync(
        string profileName, string bucket, string key, CancellationToken cancellationToken);

    /// <summary>Lists up to <paramref name="maxKeys"/> object keys under <paramref name="prefix"/>, paginated via S3's own continuation token.</summary>
    Task<S3ListPage> ListObjectsAsync(
        string profileName, string bucket, string? prefix, string? continuationToken, int maxKeys, CancellationToken cancellationToken);
}
