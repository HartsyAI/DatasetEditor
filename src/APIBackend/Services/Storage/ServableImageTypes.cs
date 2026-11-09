namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// The file types the file-serving endpoints (ServeDatasetFile, GetS3File) will hand out, keyed
/// by extension. An allow-list, not a fallback: anything else used to be served as
/// <c>application/octet-stream</c>, so a folder-import dataset pointed at a directory holding a
/// DataProtection key ring or a config file exposed it to every reader of the dataset, and to
/// everyone when the dataset is public (audit F-1). These are the formats ingestion keeps as
/// images (folder import, ZIP extraction, the S3 listing filter).
/// </summary>
public static class ServableImageTypes
{
    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
    };

    /// <summary>
    /// The content type for a servable image path or S3 key, or false when its extension is not
    /// on the allow-list (including no extension at all).
    /// </summary>
    public static bool TryGetContentType(string pathOrKey, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? contentType)
    {
        return ContentTypesByExtension.TryGetValue(Path.GetExtension(pathOrKey), out contentType);
    }
}
