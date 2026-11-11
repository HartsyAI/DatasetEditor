using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DatasetStudio.APIBackend.Models;

public sealed record HuggingFaceDatasetProfile
{
    public string Repository { get; init; } = string.Empty;

    public IReadOnlyList<HuggingFaceDatasetFile> DataFiles { get; init; } = System.Array.Empty<HuggingFaceDatasetFile>();

    public IReadOnlyList<HuggingFaceDatasetFile> ImageFiles { get; init; } = System.Array.Empty<HuggingFaceDatasetFile>();

    public bool HasDataFiles => DataFiles.Count > 0;

    public bool HasImageFiles => ImageFiles.Count > 0;

    public static HuggingFaceDatasetProfile FromDatasetInfo(string repository, HuggingFaceDatasetInfo info)
    {
        List<HuggingFaceDatasetFile> dataFiles = info.Files
            .Where(f => f.Type == "csv" || f.Type == "json" || f.Type == "parquet")
            .ToList();

        List<HuggingFaceDatasetFile> imageFiles = info.Files
            .Where(f =>
            {
                string extension = Path.GetExtension(f.Path).ToLowerInvariant();
                return extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".webp" || extension == ".gif" || extension == ".bmp";
            })
            .ToList();

        return new HuggingFaceDatasetProfile
        {
            Repository = repository,
            DataFiles = dataFiles,
            ImageFiles = imageFiles
        };
    }
}
