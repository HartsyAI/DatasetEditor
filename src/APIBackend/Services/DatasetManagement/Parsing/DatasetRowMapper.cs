using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement.Parsing;

/// <summary>
/// Maps one tabular row (CSV, TSV or Parquet) to an item. Column names are matched loosely and
/// every column is kept in the item's metadata. Shared so a Parquet file and a CSV export of
/// the same data produce the same items.
/// </summary>
public static class DatasetRowMapper
{
    /// <summary>Maps a row whose keys are lower-case column names.</summary>
    /// <param name="datasetId">The dataset the item belongs to.</param>
    /// <param name="row">Column name (lower case) to value. Kept as the item's metadata.</param>
    public static DatasetItemDto Map(Guid datasetId, Dictionary<string, string> row)
    {
        string imageUrl = row.GetValueOrDefault("image_url")
            ?? row.GetValueOrDefault("imageurl")
            ?? row.GetValueOrDefault("url")
            ?? row.GetValueOrDefault("image")
            ?? string.Empty;

        string title = row.GetValueOrDefault("title")
            ?? row.GetValueOrDefault("name")
            ?? row.GetValueOrDefault("caption")
            ?? row.GetValueOrDefault("text")
            ?? $"Item {Guid.NewGuid()}";

        string? description = row.GetValueOrDefault("description")
            ?? row.GetValueOrDefault("desc")
            ?? row.GetValueOrDefault("caption");

        string externalId = row.GetValueOrDefault("id")
            ?? row.GetValueOrDefault("image_id")
            ?? row.GetValueOrDefault("item_id")
            ?? Guid.NewGuid().ToString();

        int.TryParse(row.GetValueOrDefault("width") ?? "0", out int width);
        int.TryParse(row.GetValueOrDefault("height") ?? "0", out int height);

        List<string> tags = new();
        if (row.TryGetValue("tags", out string? tagsText) && !string.IsNullOrEmpty(tagsText))
        {
            tags = tagsText.Split(',', ';').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        return new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            ExternalId = externalId,
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = row,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
