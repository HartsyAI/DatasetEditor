using System.Text;

namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// The single source of truth for where a dataset's locally-stored files live on disk
/// (<c>{Storage:DatasetRootPath}/{slug}-{first 8 hex chars of the id}/</c>). Both the writer
/// side (ingestion — folder scans, ZIP extraction) and the reader side
/// (DatasetEndpoints.ServeDatasetFile) must agree on this exact scheme; it previously lived
/// only as a private helper on the endpoints class, so ingestion had no way to place files
/// where the serving endpoint would ever look for them.
/// </summary>
public static class DatasetStorageConventions
{
    /// <summary>Gets (and ensures the existence of) the storage folder for a dataset's local files.</summary>
    public static string GetDatasetFolderPath(string datasetRootPath, Guid datasetId, string datasetName)
    {
        string root = Path.GetFullPath(datasetRootPath);
        Directory.CreateDirectory(root);

        string slug = Slugify(datasetName);
        string shortId = datasetId.ToString("N")[..8];
        string folderName = $"{slug}-{shortId}";
        return Path.Combine(root, folderName);
    }

    /// <summary>Converts a name to a URL-friendly slug.</summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "dataset";
        }

        value = value.Trim().ToLowerInvariant();
        StringBuilder sb = new(value.Length);
        bool previousDash = false;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                previousDash = false;
            }
            else if (c is ' ' or '-' or '_' or '.')
            {
                if (!previousDash && sb.Length > 0)
                {
                    sb.Append('-');
                    previousDash = true;
                }
            }
        }

        if (sb.Length == 0)
        {
            return "dataset";
        }

        if (sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
