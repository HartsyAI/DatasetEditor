using System.Text.Json;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Parquet;
using Parquet.Data;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Reads dataset items from Parquet shards: cursor pages, single items, counts. A page decodes
/// only the row groups it returns rows from; groups before the cursor are skipped by their
/// footer row counts.
/// </summary>
public class ParquetItemReader
{
    /// <summary>A shard's unfiltered row count and the file version it was read from.</summary>
    private readonly record struct CachedRowCount(long Length, DateTime LastWriteUtc, long Rows);

    private readonly string _dataDirectory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedRowCount> _rowCountCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the ParquetItemReader.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files are stored.</param>
    public ParquetItemReader(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    /// <summary>
    /// Parses a page cursor of the form "shardIndex:rowIndex". Both parts must be non-negative
    /// integers: a negative shard index would index <c>shardFiles[-1]</c>, and a cursor from
    /// another dataset is not detectable here (cursors are not bound to a dataset).
    /// </summary>
    /// <param name="cursor">The cursor to parse.</param>
    /// <param name="shardIndex">The parsed shard index, or 0 when parsing fails.</param>
    /// <param name="rowIndex">The parsed row index, or 0 when parsing fails.</param>
    /// <returns>True when the cursor is well formed.</returns>
    public static bool TryParseCursor(string? cursor, out int shardIndex, out int rowIndex)
    {
        shardIndex = 0;
        rowIndex = 0;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        string[] parts = cursor.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int shard) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int row))
        {
            return false;
        }

        shardIndex = shard;
        rowIndex = row;
        return true;
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

        // A malformed cursor used to fall back silently to 0:0, so a client resuming a
        // paginated read restarted from the top with no signal (audit G-3). Reject it instead.
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryParseCursor(cursor, out startShardIndex, out startRowIndex))
            {
                throw new ArgumentException($"Malformed cursor '{cursor}'; expected 'shardIndex:rowIndex'.", nameof(cursor));
            }
        }

        var items = new List<DatasetItemDto>();
        // Where the next page starts. Past the last shard means there is no next page.
        int nextShardIndex = shardFiles.Length;
        int nextRowIndex = 0;

        // Read from shards until we have enough items
        for (int i = startShardIndex; i < shardFiles.Length && items.Count < pageSize; i++)
        {
            ShardRead read = await ReadFromShardAsync(
                shardFiles[i],
                filter,
                i == startShardIndex ? startRowIndex : 0,
                pageSize - items.Count,
                cancellationToken);

            items.AddRange(read.Items);

            // RowsExamined is the absolute within-shard row to resume at (see ReadFromShardAsync).
            // Comparing it with the shard's row count, rather than guessing from how many items
            // came back, is what stops a page that fills exactly at the end of the data from
            // emitting a cursor to an empty page (audit P-10).
            if (read.RowsExamined >= read.ShardRowCount)
            {
                nextShardIndex = i + 1;
                nextRowIndex = 0;
            }
            else
            {
                nextShardIndex = i;
                nextRowIndex = read.RowsExamined;
            }
        }

        string? nextCursor = nextShardIndex < shardFiles.Length ? $"{nextShardIndex}:{nextRowIndex}" : null;
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
            ShardRead read = await ReadFromShardAsync(file, null, 0, int.MaxValue, cancellationToken);
            allItems.AddRange(read.Items);
        }

        return allItems;
    }

    /// <summary>
    /// Gets the shard indices that currently exist for a dataset, in order.
    /// Used for shard-scoped updates so only affected shards are rewritten.
    /// </summary>
    public IReadOnlyList<int> GetShardIndices(Guid datasetId)
    {
        var files = GetShardFiles(datasetId);
        var indices = new List<int>(files.Length);
        foreach (var file in files)
        {
            if (ParquetSchemaDefinition.TryParseFileName(Path.GetFileName(file), out _, out var shardIndex))
            {
                indices.Add(shardIndex);
            }
        }
        return indices;
    }

    /// <summary>
    /// Reads every item from a single shard (used to rewrite that shard in place).
    /// </summary>
    public async Task<List<DatasetItemDto>> ReadShardAsync(
        Guid datasetId,
        int shardIndex,
        CancellationToken cancellationToken = default)
    {
        var fileName = ParquetSchemaDefinition.GetShardFileName(datasetId, shardIndex);
        var filePath = Path.Combine(_dataDirectory, fileName);
        if (!File.Exists(filePath))
        {
            return new List<DatasetItemDto>();
        }
        ShardRead read = await ReadFromShardAsync(filePath, null, 0, int.MaxValue, cancellationToken);
        return read.Items;
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
    /// <returns>
    /// The matched items, plus <c>RowsExamined</c> — the within-shard row index (0-based,
    /// exclusive end) the next read should skip to. This used to be inferred by the caller as
    /// <c>skipRows + items.Count</c>, which is only correct when every examined row matches
    /// (no filter, or a filter that happens to match everything): items.Count is the count of
    /// rows that PASSED the filter, not the count of rows actually looked at, so filtered reads
    /// undercounted how far into the shard they'd really gotten and the resulting cursor would
    /// re-scan (and re-return) rows the caller had already consumed. RowsExamined instead
    /// counts every row visited — skipped, filtered out, or matched — so it's accurate
    /// regardless of how selective the filter is.
    /// </returns>
    private async Task<ShardRead> ReadFromShardAsync(
        string filePath,
        FilterRequest? filter,
        int skipRows,
        int takeRows,
        CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();

        using var stream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);

        long[] groupRowCounts = GetRowGroupCounts(reader);
        long shardRowCount = groupRowCounts.Sum();
        int rowsExamined = 0;

        for (int i = 0; i < groupRowCounts.Length && items.Count < takeRows; i++)
        {
            int rowCount = (int)groupRowCounts[i];
            int groupStart = rowsExamined;

            // A group that ends before the cursor is skipped from the footer's row count, without
            // decoding any of its columns. Every group used to be fully decoded and its rows
            // skipped one by one, so page N cost O(offset) (audit P-1). skipRows counts rows
            // examined, filtered or not, so this holds for filtered reads too.
            if (groupStart + rowCount <= skipRows)
            {
                rowsExamined += rowCount;
                continue;
            }

            using var groupReader = reader.OpenRowGroupReader(i);
            var columns = await ReadAllColumnsAsync(groupReader, cancellationToken);

            int firstRow = Math.Max(0, skipRows - groupStart);
            rowsExamined = groupStart + firstRow;
            for (int row = firstRow; row < rowCount && items.Count < takeRows; row++)
            {
                rowsExamined++;

                var item = CreateItemFromRow(columns, row);
                if (filter != null && !MatchesFilter(item, filter))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        return new ShardRead(items, rowsExamined, shardRowCount);
    }

    /// <summary>The result of reading part of one shard.</summary>
    /// <param name="Items">Rows that passed the filter.</param>
    /// <param name="RowsExamined">The within-shard row index the next read should start at.</param>
    /// <param name="ShardRowCount">Rows in the whole shard.</param>
    private sealed record ShardRead(List<DatasetItemDto> Items, int RowsExamined, long ShardRowCount);

    /// <summary>Row count of each row group, from the footer (no column data is read).</summary>
    private static long[] GetRowGroupCounts(ParquetReader reader)
    {
        long[] counts = new long[reader.RowGroupCount];
        for (int i = 0; i < counts.Length; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            counts[i] = groupReader.RowCount;
        }
        return counts;
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
            // Fast path: the footer's row counts, cached per file version. A shard is only ever
            // replaced or appended to, both of which change its length or write time.
            FileInfo info = new(filePath);
            if (_rowCountCache.TryGetValue(filePath, out CachedRowCount cached)
                && cached.Length == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc)
            {
                return cached.Rows;
            }

            using var stream = File.OpenRead(filePath);
            using var reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);
            long count = GetRowGroupCounts(reader).Sum();
            _rowCountCache[filePath] = new CachedRowCount(info.Length, info.LastWriteTimeUtc, count);
            return count;
        }

        // Need to read and filter
        ShardRead read = await ReadFromShardAsync(filePath, filter, 0, int.MaxValue, cancellationToken);
        return read.Items.Count;
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
