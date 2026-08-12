namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>Looks up configured S3 storage profiles by name and enforces their bucket allow-list.</summary>
public interface IS3StorageProfileResolver
{
    S3StorageProfileConfig? GetProfile(string profileName);

    /// <summary>True if <paramref name="bucket"/> is in the named profile's AllowedBuckets list.</summary>
    bool IsBucketAllowed(string profileName, string bucket);
}
