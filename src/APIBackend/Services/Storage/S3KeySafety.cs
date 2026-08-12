namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Validates that an S3 object key genuinely belongs to a dataset's configured prefix before
/// any GetObjectAsync call touches it. This is the S3 equivalent of ServeDatasetFile's
/// containment-prefix check for local paths -- without it, a dataset scoped to
/// <c>prefix="photos/"</c> could be asked for a key like <c>photos-other-tenant/secret.jpg</c>
/// (the same sibling-prefix false-positive class Phase 1 hardened the local-disk check
/// against) or an absolute-looking key that never belonged to this dataset's bucket region.
/// </summary>
public static class S3KeySafety
{
    public static bool IsKeyWithinPrefix(string? prefix, string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(prefix))
        {
            // No prefix configured -- the whole bucket is the dataset's scope.
            return true;
        }

        string normalizedPrefix = prefix.EndsWith('/') ? prefix : prefix + "/";
        return key.StartsWith(normalizedPrefix, StringComparison.Ordinal) || key == prefix;
    }
}
