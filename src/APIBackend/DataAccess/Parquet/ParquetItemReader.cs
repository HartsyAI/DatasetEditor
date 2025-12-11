using System.Text.Json;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Parquet;
using Parquet.Data;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Reads dataset items from Parquet files with support for filtering, pagination, and column projection.
/// Supports parallel reading of multiple shards for optimal performance.
/// </summary>
public class ParquetItemReader
{
    private readonly string _dataDirectory;

    /// <summary>
    /// Initializes a new instance of the ParquetItemReader.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files are stored.</param>
    public ParquetItemReader(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    /// <summary>
    /// Reads a page of items from Parquet files with cursor-based pagination.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="filter">Optional filter criteria.</param>
    /// <param name="cursor">Optional cursor for pagination (format: "shardIndex:rowIndex").</param>
    /// <param name="pageSize">Number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of items and next cursor.</returns>
    public async Task<(List<DatasetItemDto> Items, string? NextCursor)> ReadPageAsync(
        Guid datasetId,
        FilterRequest? filter = null,
        string? cursor = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var shardFiles = GetShardFiles(datasetId);
        if (shardFiles.Length == 0)
            return (new List<DatasetItemDto>(), null);

        // Parse cursor
        int startShardIndex = 0;
        int startRowIndex = 0;

        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var shardIdx) &&
                int.TryParse(parts[1], out var rowIdx))
            {
                startShardIndex = shardIdx;
                startRowIndex = rowIdx;
            }
        }

        var items = new List<DatasetItemDto>();
        int currentShardIndex = startShardIndex;
        int currentRowIndex = startRowIndex;

        // Read from shards until we have enough items
        for (int i = startShardIndex; i < shardFiles.Length && items.Count < pageSize; i++)
        {
            var shardItems = await ReadFromShardAsync(
                shardFiles[i],
                filter,
                i == startShardIndex ? startRowIndex : 0,
                pageSize - items.Count,
                cancellationToken);

            items.AddRange(shardItems);

            currentShardIndex = i;
            currentRowIndex = i == startShardIndex ? startRowIndex + shardItems.Count : shardItems.Count;

            // If we got fewer items than requested from this shard, move to next shard
            if (shardItems.Count < pageSize - items.Count + shardItems.Count)
            {
                currentShardIndex++;
                currentRowIndex = 0;
            }
        }

        // Create next cursor
        string? nextCursor = null;
        if (items.Count == pageSize && currentShardIndex < shardFiles.Length)
        {
            nextCursor = $"{currentShardIndex}:{currentRowIndex}";
        }

        return (items, nextCursor);
    }

    /// <summary>
    /// Reads a specific item by ID from Parquet files.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="itemId">The item ID to find.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item if found, null otherwise.</returns>
    public async Task<DatasetItemDto?> ReadItemAsync(
        Guid datasetId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var shardFiles = GetShardFiles(datasetId);

        // Search all shards in parallel for better performance
        var tasks = shardFiles.Select(file => FindItemInShardAsync(file, itemId, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.FirstOrDefault(item => item != null);
    }

    /// <summary>
    /// Counts total items in a dataset, optionally with filters.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="filter">Optional filter criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total count of items.</returns>
    public async Task<long> CountAsync(
        Guid datasetId,
        FilterRequest? filter = null,
        CancellationToken cancellationToken = default)
    {
        var shardFiles = GetShardFiles(datasetId);
        if (shardFiles.Length == 0)
            return 0;

        // Count in parallel across all shards
        var tasks = shardFiles.Select(file => CountInShardAsync(file, filter, cancellationToken));
        var counts = await Task.WhenAll(tasks);

        return counts.Sum();
    }

    /// <summary>
    /// Reads all items from a dataset (use with caution for large datasets).
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All items in the dataset.</returns>
    public async Task<List<DatasetItemDto>> ReadAllAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var shardFiles = GetShardFiles(datasetId);
        var allItems = new List<DatasetItemDto>();

        foreach (var file in shardFiles)
        {
            var items = await ReadFromShardAsync(file, null, 0, int.MaxValue, cancellationToken);
            allItems.AddRange(items);
        }

        return allItems;
    }

    /// <summary>
    /// Gets all shard files for a dataset, sorted by shard index.
    /// </summary>
    private string[] GetShardFiles(Guid datasetId)
    {
        var pattern = $"dataset_{datasetId:N}_shard_*.parquet";
        var files = Directory.GetFiles(_dataDirectory, pattern);

        // Sort by shard index
        return files.OrderBy(f =>
        {
            var fileName = Path.GetFileName(f);
            if (ParquetSchemaDefinition.TryParseFileName(fileName, out _, out var shardIndex))
                return shardIndex;
            return int.MaxValue;
        }).ToArray();
    }

    /// <summary>
    /// Reads items from a single shard file.
    /// </summary>
    private async Task<List<DatasetItemDto>> ReadFromShardAsync(
        string filePath,
        FilterRequest? filter,
        int skipRows,
        int takeRows,
        CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();

        using var stream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);

        int rowsSkipped = 0;

        // Read all row groups in the file
        for (int i = 0; i < reader.RowGroupCount && items.Count < takeRows; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            var rowCount = (int)groupReader.RowCount;

            // Read all columns
            var columns = await ReadAllColumnsAsync(groupReader, cancellationToken);

            // Process rows
            for (int row = 0; row < rowCount && items.Count < takeRows; row++)
            {
                if (rowsSkipped < skipRows)
                {
                    rowsSkipped++;
                    continue;
                }

                var item = CreateItemFromRow(columns, row);

                // Apply filters
                if (filter != null && !MatchesFilter(item, filter))
                    continue;

                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Finds a specific item in a shard file.
    /// </summary>
    private async Task<DatasetItemDto?> FindItemInShardAsync(
        string filePath,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);

        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            var rowCount = (int)groupReader.RowCount;

            // Only read ID column for initial search
            var idColumn = await groupReader.ReadColumnAsync(ParquetSchemaDefinition.Schema.DataFields[0], cancellationToken);
            var ids = (Guid[])idColumn.Data;

            // Find matching row
            for (int row = 0; row < rowCount; row++)
            {
                if (ids[row] == itemId)
                {
                    // Found it - now read all columns for this row group
                    var columns = await ReadAllColumnsAsync(groupReader, cancellationToken);
                    return CreateItemFromRow(columns, row);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Counts items in a single shard file.
    /// </summary>
    private async Task<long> CountInShardAsync(
        string filePath,
        FilterRequest? filter,
        CancellationToken cancellationToken)
    {
        if (filter == null)
        {
            // Fast path - just count rows without reading data
            using var stream = File.OpenRead(filePath);
            using var reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);

            long count = 0;
            for (int i = 0; i < reader.RowGroupCount; i++)
            {
                using var groupReader = reader.OpenRowGroupReader(i);
                count += groupReader.RowCount;
            }
            return count;
        }

        // Need to read and filter
        var items = await ReadFromShardAsync(filePath, filter, 0, int.MaxValue, cancellationToken);
        return items.Count;
    }

    /// <summary>
    /// Reads all columns from a row group.
    /// </summary>
    private async Task<Dictionary<string, Array>> ReadAllColumnsAsync(
        ParquetRowGroupReader groupReader,
        CancellationToken cancellationToken)
    {
        var columns = new Dictionary<string, Array>();

        foreach (var field in ParquetSchemaDefinition.Schema.DataFields)
        {
            var column = await groupReader.ReadColumnAsync(field, cancellationToken);
            columns[field.Name] = column.Data;
        }

        return columns;
    }

    /// <summary>
    /// Creates a DatasetItemDto from columnar data at a specific row index.
    /// </summary>
    private DatasetItemDto CreateItemFromRow(Dictionary<string, Array> columns, int row)
    {
        var ids = (Guid[])columns["id"];
        var datasetIds = (Guid[])columns["dataset_id"];
        var externalIds = (string[])columns["external_id"];
        var titles = (string[])columns["title"];
        var descriptions = (string[])columns["description"];
        var imageUrls = (string[])columns["image_url"];
        var thumbnailUrls = (string[])columns["thumbnail_url"];
        var widths = (int[])columns["width"];
        var heights = (int[])columns["height"];
        var tagsJson = (string[])columns["tags_json"];
        var isFavorites = (bool[])columns["is_favorite"];
        var metadataJson = (string[])columns["metadata_json"];
        var createdAts = (DateTime[])columns["created_at"];
        var updatedAts = (DateTime[])columns["updated_at"];

        return new DatasetItemDto
        {
            Id = ids[row],
            DatasetId = datasetIds[row],
            ExternalId = externalIds[row],
            Title = titles[row],
            Description = descriptions[row],
            ImageUrl = imageUrls[row],
            ThumbnailUrl = thumbnailUrls[row],
            Width = widths[row],
            Height = heights[row],
            Tags = JsonSerializer.Deserialize<List<string>>(tagsJson[row]) ?? new List<string>(),
            IsFavorite = isFavorites[row],
            Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson[row]) ?? new Dictionary<string, string>(),
            CreatedAt = createdAts[row],
            UpdatedAt = updatedAts[row]
        };
    }

    /// <summary>
    /// Checks if an item matches the filter criteria.
    /// </summary>
    private bool MatchesFilter(DatasetItemDto item, FilterRequest filter)
    {
        // Search query
        if (!string.IsNullOrEmpty(filter.SearchQuery))
        {
            var query = filter.SearchQuery.ToLowerInvariant();
            if (!item.Title.ToLowerInvariant().Contains(query) &&
                !(item.Description?.ToLowerInvariant().Contains(query) ?? false) &&
                !item.Tags.Any(t => t.ToLowerInvariant().Contains(query)))
            {
                return false;
            }
        }

        // Tags filter
        if (filter.Tags.Length > 0)
        {
            if (!filter.Tags.All(tag => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                return false;
        }

        // Date range
        if (filter.DateFrom.HasValue && item.CreatedAt < filter.DateFrom.Value)
            return false;

        if (filter.DateTo.HasValue && item.CreatedAt > filter.DateTo.Value)
            return false;

        // Favorites filter
        if (filter.FavoritesOnly == true && !item.IsFavorite)
            return false;

        // Dimension filters
        if (filter.MinWidth.HasValue && item.Width < filter.MinWidth.Value)
            return false;

        if (filter.MaxWidth.HasValue && item.Width > filter.MaxWidth.Value)
            return false;

        if (filter.MinHeight.HasValue && item.Height < filter.MinHeight.Value)
            return false;

        if (filter.MaxHeight.HasValue && item.Height > filter.MaxHeight.Value)
            return false;

        // Aspect ratio filters
        if (filter.MinAspectRatio.HasValue || filter.MaxAspectRatio.HasValue)
        {
            var aspectRatio = item.Height > 0 ? (double)item.Width / item.Height : 0.0;

            if (filter.MinAspectRatio.HasValue && aspectRatio < filter.MinAspectRatio.Value)
                return false;

            if (filter.MaxAspectRatio.HasValue && aspectRatio > filter.MaxAspectRatio.Value)
                return false;
        }

        // Metadata filters
        if (!string.IsNullOrEmpty(filter.Photographer))
        {
            if (!item.Metadata.TryGetValue("photographer", out var photographer) ||
                !photographer.Equals(filter.Photographer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(filter.Location))
        {
            if (!item.Metadata.TryGetValue("location", out var location) ||
                !location.Equals(filter.Location, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
