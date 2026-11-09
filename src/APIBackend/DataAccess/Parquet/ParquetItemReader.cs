using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Parquet;
using Parquet.Data;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Reads dataset items from the segments a <see cref="ParquetManifest"/> lists: cursor pages,
/// single items (through each segment's <see cref="SegmentIdIndex"/>), counts and whole
/// segments. A page decodes only the row groups it returns rows from.
/// </summary>
public class ParquetItemReader
{
    /// <summary>Prefix of a cursor: an absolute row offset into the dataset.</summary>
    private const string OffsetCursorPrefix = "r";

    private readonly string _dataDirectory;

    /// <summary>
    /// Initializes a new instance of the ParquetItemReader.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files are stored.</param>
    public ParquetItemReader(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    private string PathOf(string fileName) => Path.Combine(_dataDirectory, fileName);

    /// <summary>The cursor for an absolute row offset.</summary>
    public static string FormatCursor(long offset) => OffsetCursorPrefix + offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a page cursor into an absolute row offset. Cursors are <c>r{offset}</c>: an offset
    /// stays valid when compaction merges segments, which a segment-relative position would not.
    /// The older <c>"shardIndex:rowIndex"</c> form is still accepted (shard i is segment i of a
    /// dataset adopted from shards). Anything else is malformed, so a client never silently
    /// restarts from the top (audit G-3).
    /// </summary>
    public static bool TryParseCursor(string? cursor, ParquetManifest manifest, out long offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        if (cursor.StartsWith(OffsetCursorPrefix, StringComparison.Ordinal))
        {
            return long.TryParse(cursor.AsSpan(OffsetCursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
        }

        string[] parts = cursor.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int segment)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long row))
        {
            return false;
        }

        long[] starts = manifest.SegmentStarts();
        offset = segment < starts.Length ? starts[segment] + row : manifest.TotalRows;
        return true;
    }

    /// <summary>Whether a cursor has a valid form, without resolving it against a dataset.</summary>
    public static bool IsWellFormedCursor(string cursor) => TryParseCursor(cursor, ParquetManifest.Empty, out _);

    /// <summary>
    /// Reads a page starting at the cursor. Returns no cursor when the page reaches the last row
    /// (audit P-10).
    /// </summary>
    public async Task<(List<DatasetItemDto> Items, string? NextCursor)> ReadPageAsync(
        ParquetManifest manifest,
        FilterRequest? filter = null,
        string? cursor = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        long startOffset = 0;
        if (!string.IsNullOrEmpty(cursor) && !TryParseCursor(cursor, manifest, out startOffset))
        {
            throw new ArgumentException($"Malformed cursor '{cursor}'.", nameof(cursor));
        }

        List<DatasetItemDto> items = new();
        long[] starts = manifest.SegmentStarts();
        long nextOffset = manifest.TotalRows;

        for (int i = 0; i < manifest.Segments.Count && items.Count < pageSize; i++)
        {
            ParquetSegment segment = manifest.Segments[i];
            long segmentEnd = starts[i] + segment.Rows;
            if (segmentEnd <= startOffset)
            {
                continue;
            }

            int skipRows = (int)Math.Max(0, startOffset - starts[i]);
            ShardRead read = await ReadFromShardAsync(PathOf(segment.File), filter, skipRows, pageSize - items.Count, cancellationToken);
            items.AddRange(read.Items);
            nextOffset = starts[i] + read.RowsExamined;
        }

        string? nextCursor = nextOffset < manifest.TotalRows ? FormatCursor(nextOffset) : null;
        return (items, nextCursor);
    }

    /// <summary>
    /// Reads the item with this id from whichever segment holds it, using each segment's id index.
    /// Every segment must have its index (see <see cref="ParquetItemRepository"/>).
    /// </summary>
    public async Task<DatasetItemDto?> ReadItemAsync(
        ParquetManifest manifest,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        foreach (ParquetSegment segment in manifest.Segments)
        {
            int? row = SegmentIdIndex.Find(PathOf(SegmentIdIndex.FileNameFor(segment.File)), itemId);
            if (row is int found)
            {
                return await ReadRowAsync(PathOf(segment.File), found, cancellationToken);
            }
        }
        return null;
    }

    /// <summary>Counts items: the manifest's row total, or a scan when filtered.</summary>
    public async Task<long> CountAsync(
        ParquetManifest manifest,
        FilterRequest? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (filter == null)
        {
            return manifest.TotalRows;
        }

        long count = 0;
        foreach (ParquetSegment segment in manifest.Segments)
        {
            ShardRead read = await ReadFromShardAsync(PathOf(segment.File), filter, 0, int.MaxValue, cancellationToken);
            count += read.Items.Count;
        }
        return count;
    }

    /// <summary>Reads every item of the dataset (statistics; use with care on large datasets).</summary>
    public async Task<List<DatasetItemDto>> ReadAllAsync(
        ParquetManifest manifest,
        CancellationToken cancellationToken = default)
    {
        List<DatasetItemDto> allItems = new();
        foreach (ParquetSegment segment in manifest.Segments)
        {
            ShardRead read = await ReadFromShardAsync(PathOf(segment.File), null, 0, int.MaxValue, cancellationToken);
            allItems.AddRange(read.Items);
        }
        return allItems;
    }

    /// <summary>
    /// Yields a segment one row group at a time, so rewriting or merging segments holds one group
    /// in memory, never a whole file (audit P-3).
    /// </summary>
    public async IAsyncEnumerable<List<DatasetItemDto>> ReadGroupsAsync(
        string segmentPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(segmentPath);
        using ParquetReader reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);
        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using ParquetRowGroupReader group = reader.OpenRowGroupReader(g);
            Dictionary<string, Array> columns = await ReadAllColumnsAsync(group, cancellationToken);
            int rowCount = (int)group.RowCount;
            List<DatasetItemDto> items = new(rowCount);
            for (int row = 0; row < rowCount; row++)
            {
                items.Add(CreateItemFromRow(columns, row));
            }
            yield return items;
        }
    }

    /// <summary>Item ids of a segment in row order, reading only the id column.</summary>
    public async Task<List<Guid>> ReadIdsAsync(string segmentPath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(segmentPath);
        using ParquetReader reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);
        List<Guid> ids = new();
        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using ParquetRowGroupReader group = reader.OpenRowGroupReader(g);
            DataColumn column = await group.ReadColumnAsync(ParquetSchemaDefinition.Schema.DataFields[0], cancellationToken);
            ids.AddRange((Guid[])column.Data);
        }
        return ids;
    }

    /// <summary>Reads one row by its position in the segment, decoding only its row group.</summary>
    private async Task<DatasetItemDto?> ReadRowAsync(string segmentPath, int row, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(segmentPath);
        using ParquetReader reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);
        long groupStart = 0;
        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using ParquetRowGroupReader group = reader.OpenRowGroupReader(g);
            if (row < groupStart + group.RowCount)
            {
                Dictionary<string, Array> columns = await ReadAllColumnsAsync(group, cancellationToken);
                return CreateItemFromRow(columns, (int)(row - groupStart));
            }
            groupStart += group.RowCount;
        }
        return null;
    }

    /// <summary>
    /// Reads rows from one segment file, starting <paramref name="skipRows"/> rows in.
    /// </summary>
    /// <returns>
    /// The matched items, plus <c>RowsExamined</c>: the within-file row index the next read
    /// should start at. It counts every row visited (skipped, filtered out or matched), so a
    /// filtered read resumes where it really stopped rather than re-returning rows.
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
            // decoding any of its columns (audit P-1). skipRows counts rows examined, filtered or
            // not, so this holds for filtered reads too.
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

    /// <summary>The result of reading part of one segment.</summary>
    /// <param name="Items">Rows that passed the filter.</param>
    /// <param name="RowsExamined">The within-segment row index the next read should start at.</param>
    /// <param name="ShardRowCount">Rows in the whole segment.</param>
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
