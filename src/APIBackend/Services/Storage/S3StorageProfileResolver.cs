namespace DatasetStudio.APIBackend.Services.Storage;

public sealed class S3StorageProfileResolver : IS3StorageProfileResolver
{
    private readonly Dictionary<string, S3StorageProfileConfig> _profilesByName;

    public S3StorageProfileResolver(IEnumerable<S3StorageProfileConfig> profiles)
    {
        _profilesByName = profiles.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public S3StorageProfileConfig? GetProfile(string profileName)
    {
        return _profilesByName.TryGetValue(profileName, out S3StorageProfileConfig? profile) ? profile : null;
    }

    public bool IsBucketAllowed(string profileName, string bucket)
    {
        S3StorageProfileConfig? profile = GetProfile(profileName);
        if (profile is null)
        {
            return false;
        }
        return profile.AllowedBuckets.Contains(bucket, StringComparer.Ordinal);
    }
}
