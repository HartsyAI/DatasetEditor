using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Parquet storage for local dataset items. Each dataset is a <see cref="ParquetManifest"/> over
/// immutable segment files, each with a <see cref="SegmentIdIndex"/>. Every write builds new
/// files first and commits by replacing the manifest (or, for an edit, by renaming a complete
/// rewritten segment over the old one), so a crash never leaves a dataset half-written.
/// </summary>
/// <remarks>
/// All operations on one dataset are serialised by a per-dataset lock; different datasets never
/// block each other.
/// </remarks>
public class ParquetItemRepository : IDatasetItemRepository, IDisposable
{
    /// <summary>Segments smaller than this at the end of a dataset are candidates for merging.</summary>
    internal const long SmallSegmentRows = 250_000;

    /// <summary>How many small trailing segments trigger a merge.</summary>
    internal const int CompactionMinSegments = 8;

    private readonly ParquetItemReader _reader;
    private readonly ParquetItemWriter _writer;
    private readonly ParquetManifestStore _manifests;
    private readonly ILogger<ParquetItemRepository> _logger;
    // Per-dataset locks so ingesting/updating one dataset never blocks another.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _datasetLocks = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ParquetItemRepository.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files will be stored.</param>
    /// <param name="logger">Logger instance.</param>
    public ParquetItemRepository(string dataDirectory, ILogger<ParquetItemRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(dataDirectory);
        _reader = new ParquetItemReader(dataDirectory);
        _writer = new ParquetItemWriter(dataDirectory);
        _manifests = new ParquetManifestStore(dataDirectory);
    }

    private SemaphoreSlim GetDatasetLock(Guid datasetId)
        => _datasetLocks.GetOrAdd(datasetId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Runs <paramref name="action"/> holding the dataset's lock, with its manifest.</summary>
    private async Task<T> WithDatasetAsync<T>(Guid datasetId, Func<ParquetManifest, Task<T>> action, CancellationToken cancellationToken)
    {
        SemaphoreSlim datasetLock = GetDatasetLock(datasetId);
        await datasetLock.WaitAsync(cancellationToken);
        try
        {
            ParquetManifest manifest = await _manifests.LoadAsync(datasetId, cancellationToken);
            return await action(manifest);
        }
        finally
        {
            datasetLock.Release();
        }
    }

    /// <summary>
    /// Adds items as a new segment and commits it to the manifest. The item count is the
    /// manifest's row total, so there is no separate counter to go stale (audit P-7).
    /// </summary>
    public async Task AddRangeAsync(
        Guid datasetId,
        IEnumerable<DatasetItemDto> items,
        CancellationToken cancellationToken = default)
    {
        List<DatasetItemDto> itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        await WithDatasetAsync(datasetId, async manifest =>
        {
            _manifests.SweepOrphans(datasetId, manifest);

            string file = ParquetManifestStore.SegmentFileName(datasetId, manifest.NextSegment);
            List<Guid> ids = await _writer.WriteSegmentAsync(_manifests.PathOf(file), itemList, cancellationToken);
            await SegmentIdIndex.WriteAsync(_manifests.PathOf(SegmentIdIndex.FileNameFor(file)), ids, cancellationToken);

            ParquetManifest next = new(
                manifest.NextSegment + 1,
                manifest.Segments.Append(new ParquetSegment(file, itemList.Count)).ToList());
            // The commit. Not cancellable: once the segment is complete it should be published.
            await _manifests.SaveAsync(datasetId, next, CancellationToken.None);

            _logger.LogInformation(
                "Added {Count} items to dataset {DatasetId} as {Segment}; total {Total}",
                itemList.Count, datasetId, file, next.TotalRows);

            await TryCompactTailAsync(datasetId, next);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets a page of items with optional filtering and cursor-based pagination.
    /// </summary>
    public async Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(
        Guid datasetId,
        FilterRequest? filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await WithDatasetAsync(datasetId, async manifest =>
        {
            (List<DatasetItemDto> items, string? nextCursor) = await _reader.ReadPageAsync(manifest, filter, cursor, pageSize, cancellationToken);
            return ((IReadOnlyList<DatasetItemDto>)items, nextCursor);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets an item by id within its dataset: a binary search of each segment's id index. It used
    /// to read the id column of every row group of every dataset on the server (audit P-2).
    /// </summary>
    public async Task<DatasetItemDto?> GetItemAsync(
        Guid datasetId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        return await WithDatasetAsync(datasetId, async manifest =>
        {
            await EnsureIdIndexesAsync(manifest, cancellationToken);
            return await _reader.ReadItemAsync(manifest, itemId, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets an item when the caller doesn't know its dataset: each dataset's id indexes are
    /// searched in turn. Callers that know the dataset use the overload that takes it.
    /// </summary>
    public async Task<DatasetItemDto?> GetItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        foreach (Guid datasetId in _manifests.ListDatasets())
        {
            DatasetItemDto? item = await GetItemAsync(datasetId, itemId, cancellationToken);
            if (item is not null)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>Updates a single item.</summary>
    public async Task UpdateItemAsync(
        DatasetItemDto item,
        CancellationToken cancellationToken = default)
    {
        await UpdateItemsAsync(new[] { item }, cancellationToken);
    }

    /// <summary>
    /// Replaces whole items: each one is written over the stored item with its id. Kept for
    /// callers that already hold a complete item; request-time edits use
    /// <see cref="EditItemAsync"/>, which applies the change to the current item instead.
    /// </summary>
    public async Task UpdateItemsAsync(
        IEnumerable<DatasetItemDto> items,
        CancellationToken cancellationToken = default)
    {
        foreach (IGrouping<Guid, DatasetItemDto> datasetGroup in items.GroupBy(i => i.DatasetId))
        {
            Guid datasetId = datasetGroup.Key;
            Dictionary<Guid, DatasetItemDto> replacements = datasetGroup.ToDictionary(i => i.Id);

            await WithDatasetAsync(datasetId, async manifest =>
            {
                await RewriteItemsAsync(datasetId, manifest, replacements.Keys, current => replacements[current.Id], cancellationToken);
                return true;
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Edits one item as it stands. The read, the precondition check and the rewrite all happen
    /// under the dataset's lock. The edit endpoint used to read the item, change it outside any
    /// lock and write the whole item back, so of two concurrent edits to different fields only the
    /// last one survived (audit E-4).
    /// </summary>
    public async Task<ItemEditResult> EditItemAsync(
        Guid datasetId,
        Guid itemId,
        Func<DatasetItemDto, bool>? precondition,
        Func<DatasetItemDto, DatasetItemDto> edit,
        CancellationToken cancellationToken = default)
    {
        return await WithDatasetAsync(datasetId, async manifest =>
        {
            await EnsureIdIndexesAsync(manifest, cancellationToken);
            DatasetItemDto? current = await _reader.ReadItemAsync(manifest, itemId, cancellationToken);
            if (current is null)
            {
                return new ItemEditResult(ItemEditStatus.NotFound, null);
            }
            if (precondition is not null && !precondition(current))
            {
                return new ItemEditResult(ItemEditStatus.PreconditionFailed, current);
            }

            Dictionary<Guid, DatasetItemDto> edited = await RewriteItemsAsync(datasetId, manifest, new[] { itemId }, edit, cancellationToken);
            return edited.TryGetValue(itemId, out DatasetItemDto? written)
                ? new ItemEditResult(ItemEditStatus.Updated, written)
                : new ItemEditResult(ItemEditStatus.NotFound, null);
        }, cancellationToken);
    }

    /// <summary>Edits many items of one dataset as they stand, under the dataset's lock.</summary>
    public async Task<IReadOnlyList<DatasetItemDto>> EditItemsAsync(
        Guid datasetId,
        IReadOnlyCollection<Guid> itemIds,
        Func<DatasetItemDto, DatasetItemDto> edit,
        CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return Array.Empty<DatasetItemDto>();
        }

        return await WithDatasetAsync(datasetId, async manifest =>
        {
            Dictionary<Guid, DatasetItemDto> edited = await RewriteItemsAsync(datasetId, manifest, itemIds, edit, cancellationToken);
            return (IReadOnlyList<DatasetItemDto>)edited.Values.ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Rewrites the segments holding these items, applying <paramref name="edit"/> to each one as
    /// it is read. Only the segments that hold an edited item are rewritten, one row group at a
    /// time to a temp file that is renamed over the segment. Edits used to read the whole shard (up
    /// to 10M rows) into memory and rewrite it as one row group (audit P-3). A segment's rows keep
    /// their positions, so its id index stays valid. Called with the dataset's lock held.
    /// </summary>
    /// <returns>The items as written, by id; ids the dataset doesn't have are absent.</returns>
    private async Task<Dictionary<Guid, DatasetItemDto>> RewriteItemsAsync(
        Guid datasetId,
        ParquetManifest manifest,
        IEnumerable<Guid> itemIds,
        Func<DatasetItemDto, DatasetItemDto> edit,
        CancellationToken cancellationToken)
    {
        await EnsureIdIndexesAsync(manifest, cancellationToken);
        HashSet<Guid> pending = new(itemIds);
        Dictionary<Guid, DatasetItemDto> written = new();
        int rewritten = 0;
        foreach (ParquetSegment segment in manifest.Segments)
        {
            string indexPath = _manifests.PathOf(SegmentIdIndex.FileNameFor(segment.File));
            HashSet<Guid> inSegment = pending.Where(id => SegmentIdIndex.Find(indexPath, id) is not null).ToHashSet();
            if (inSegment.Count == 0)
            {
                continue;
            }
            pending.ExceptWith(inSegment);

            // Each segment is replaced atomically (temp file, then rename). A batch cancelled
            // between segments keeps those already rewritten; each one is complete, but the
            // batch as a whole is not all-or-nothing.
            string path = _manifests.PathOf(segment.File);
            await _writer.WriteSegmentAsync(path, ApplyEdits(path, inSegment, edit, written, cancellationToken), cancellationToken);
            rewritten++;
        }

        _logger.LogInformation(
            "Updated {Count} items in dataset {DatasetId} ({Segments} segment(s) rewritten)",
            written.Count, datasetId, rewritten);
        return written;
    }

    /// <summary>
    /// Deletes all items for a dataset.
    /// </summary>
    public async Task DeleteByDatasetAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim datasetLock = GetDatasetLock(datasetId);
        await datasetLock.WaitAsync(cancellationToken);
        try
        {
            _manifests.Delete(datasetId);
            _logger.LogInformation("Deleted all items for dataset {DatasetId}", datasetId);
        }
        finally
        {
            datasetLock.Release();
        }
    }

    /// <summary>
    /// Gets the total count of items in a dataset: the manifest's row total, or a scan when filtered.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="filter">Optional filter to count only matching items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total count of items.</returns>
    public async Task<long> GetCountAsync(
        Guid datasetId,
        FilterRequest? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await WithDatasetAsync(datasetId, manifest => _reader.CountAsync(manifest, filter, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Performs bulk statistics aggregation across items.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of aggregated statistics.</returns>
    public async Task<Dictionary<string, object>> GetStatisticsAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        List<DatasetItemDto> allItems = await WithDatasetAsync(datasetId, manifest => _reader.ReadAllAsync(manifest, cancellationToken), cancellationToken);

        return new Dictionary<string, object>
        {
            ["total_items"] = allItems.Count,
            ["favorite_count"] = allItems.Count(i => i.IsFavorite),
            ["avg_width"] = allItems.Any() ? allItems.Average(i => i.Width) : 0,
            ["avg_height"] = allItems.Any() ? allItems.Average(i => i.Height) : 0,
            ["min_width"] = allItems.Any() ? allItems.Min(i => i.Width) : 0,
            ["max_width"] = allItems.Any() ? allItems.Max(i => i.Width) : 0,
            ["min_height"] = allItems.Any() ? allItems.Min(i => i.Height) : 0,
            ["max_height"] = allItems.Any() ? allItems.Max(i => i.Height) : 0,
            ["tag_counts"] = allItems
                .SelectMany(i => i.Tags)
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Writes the id index of any segment that lacks one: shards adopted from before manifests,
    /// read once through their id column. Called with the dataset's lock held.
    /// </summary>
    private async Task EnsureIdIndexesAsync(ParquetManifest manifest, CancellationToken cancellationToken)
    {
        foreach (ParquetSegment segment in manifest.Segments)
        {
            string indexPath = _manifests.PathOf(SegmentIdIndex.FileNameFor(segment.File));
            if (!File.Exists(indexPath))
            {
                List<Guid> ids = await _reader.ReadIdsAsync(_manifests.PathOf(segment.File), cancellationToken);
                await SegmentIdIndex.WriteAsync(indexPath, ids, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Merges small trailing segments into one, so a dataset built from many small appends
    /// doesn't grow without bound in files (rest of audit P-5). The merged segment is written in
    /// full, then the manifest swap publishes it and drops the inputs. Best effort: a failure
    /// leaves the committed segments as they were.
    /// </summary>
    private async Task TryCompactTailAsync(Guid datasetId, ParquetManifest manifest)
    {
        int start = manifest.Segments.Count;
        while (start > 0 && manifest.Segments[start - 1].Rows < SmallSegmentRows)
        {
            start--;
        }
        int count = manifest.Segments.Count - start;
        if (count < CompactionMinSegments)
        {
            return;
        }

        try
        {
            List<ParquetSegment> inputs = manifest.Segments.Skip(start).ToList();
            string file = ParquetManifestStore.SegmentFileName(datasetId, manifest.NextSegment);
            List<Guid> ids = await _writer.WriteSegmentAsync(_manifests.PathOf(file), ConcatGroups(inputs), CancellationToken.None);
            await SegmentIdIndex.WriteAsync(_manifests.PathOf(SegmentIdIndex.FileNameFor(file)), ids, CancellationToken.None);

            ParquetManifest next = new(
                manifest.NextSegment + 1,
                manifest.Segments.Take(start).Append(new ParquetSegment(file, ids.Count)).ToList());
            await _manifests.SaveAsync(datasetId, next, CancellationToken.None);
            _manifests.SweepOrphans(datasetId, next);
            _logger.LogInformation("Merged {Count} segments of dataset {DatasetId} into {Segment}", count, datasetId, file);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not merge segments of dataset {DatasetId}; they stay as they are", datasetId);
        }
    }

    private async IAsyncEnumerable<List<DatasetItemDto>> ConcatGroups(IReadOnlyList<ParquetSegment> segments)
    {
        foreach (ParquetSegment segment in segments)
        {
            await foreach (List<DatasetItemDto> group in _reader.ReadGroupsAsync(_manifests.PathOf(segment.File)))
            {
                yield return group;
            }
        }
    }

    /// <summary>
    /// Streams a segment's row groups with <paramref name="edit"/> applied to the listed items,
    /// recording each edited item in <paramref name="written"/>. The edit can't move an item to
    /// another id or dataset. Every edited item gets a new <c>UpdatedAt</c>, which is its version.
    /// </summary>
    private async IAsyncEnumerable<List<DatasetItemDto>> ApplyEdits(
        string segmentPath,
        IReadOnlySet<Guid> itemIds,
        Func<DatasetItemDto, DatasetItemDto> edit,
        Dictionary<Guid, DatasetItemDto> written,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        await foreach (List<DatasetItemDto> group in _reader.ReadGroupsAsync(segmentPath, cancellationToken))
        {
            for (int i = 0; i < group.Count; i++)
            {
                DatasetItemDto current = group[i];
                if (!itemIds.Contains(current.Id))
                {
                    continue;
                }

                DateTime updatedAt = ItemVersions.NextUpdatedAt(current.UpdatedAt, now);
                DatasetItemDto edited = edit(current) with
                {
                    Id = current.Id,
                    DatasetId = current.DatasetId,
                    UpdatedAt = updatedAt,
                    Version = ItemVersions.ForLocal(updatedAt)
                };
                group[i] = edited;
                written[current.Id] = edited;
            }
            yield return group;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _writer.Dispose();
        foreach (SemaphoreSlim datasetLock in _datasetLocks.Values)
        {
            datasetLock.Dispose();
        }
        _datasetLocks.Clear();

        _disposed = true;
    }
}
