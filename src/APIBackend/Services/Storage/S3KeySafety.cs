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
    /// <summary>
    /// True when <paramref name="key"/> lies under <paramref name="prefix"/> (treated as a folder)
    /// and has no <c>.</c> or <c>..</c> path segment. Only whole segments are refused: a key such as
    /// <c>photo..final.jpg</c> is a legitimate object name and used to be rejected by a plain
    /// substring test (audit S-3). Backslashes count as separators too, because an HTTP stack may
    /// turn them into slashes and then collapse the dot segment on the way to the bucket.
    /// </summary>
    public static bool IsKeyWithinPrefix(string? prefix, string key)
    {
        if (string.IsNullOrEmpty(key) || HasDotSegment(key))
        {
            return false;
        }

        string? folderPrefix = NormalizeFolderPrefix(prefix);
        if (folderPrefix is null)
        {
            // No prefix configured -- the whole bucket is the dataset's scope.
            return true;
        }

        return key.StartsWith(folderPrefix, StringComparison.Ordinal) || key == prefix;
    }

    /// <summary>
    /// The dataset prefix as a folder (ending in <c>/</c>), or null for "the whole bucket". Listing
    /// with a bare <c>images</c> prefix also returned <c>images-other-tenant/…</c>, keys the dataset
    /// then refused to serve (audit S-3), so listings go through this as well.
    /// </summary>
    public static string? NormalizeFolderPrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }
        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }

    private static bool HasDotSegment(string key)
    {
        foreach (string segment in key.Split('/', '\\'))
        {
            if (segment is "." or "..")
            {
                return true;
            }
        }
        return false;
    }
}
