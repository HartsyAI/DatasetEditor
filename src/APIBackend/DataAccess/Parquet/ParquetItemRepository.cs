using System.Collections.Concurrent;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Parquet-based implementation of IDatasetItemRepository for storing billions of dataset items.
/// Uses automatic sharding (10M items per file) for horizontal scalability.
/// </summary>
public class ParquetItemRepository : IDatasetItemRepository, IDisposable
{
    private readonly ParquetItemReader _reader;
    private readonly ParquetItemWriter _writer;
    private readonly ILogger<ParquetItemRepository> _logger;
    private readonly string _dataDirectory;
    // Per-dataset write locks so ingesting/updating one dataset never blocks another.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _datasetLocks = new();
    private readonly ConcurrentDictionary<Guid, long> _datasetItemCounts = new();
    private bool _disposed;

    /// <summary>Gets (or creates) the write lock for a single dataset.</summary>
    private SemaphoreSlim GetDatasetLock(Guid datasetId)
        => _datasetLocks.GetOrAdd(datasetId, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Initializes a new instance of the ParquetItemRepository.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files will be stored.</param>
    /// <param name="logger">Logger instance.</param>
    public ParquetItemRepository(string dataDirectory, ILogger<ParquetItemRepository> logger)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(_dataDirectory);

        _reader = new ParquetItemReader(_dataDirectory);
        _writer = new ParquetItemWriter(_dataDirectory);

        // Initialize item counts
        InitializeItemCounts();
    }

    /// <summary>
    /// Adds a range of items to a dataset.
    /// Items are automatically sharded across multiple Parquet files.
    /// </summary>
    public async Task AddRangeAsync(
        Guid datasetId,
        IEnumerable<DatasetItemDto> items,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        var datasetLock = GetDatasetLock(datasetId);
        await datasetLock.WaitAsync(cancellationToken);
        try
        {
            // Get current count to determine starting index
            long startIndex = await GetOrInitializeItemCountAsync(datasetId, cancellationToken);

            _logger.LogInformation(
                "Adding {Count} items to dataset {DatasetId} starting at index {StartIndex}",
                itemList.Count, datasetId, startIndex);

            // Write in batches for optimal performance
            var batchSize = ParquetSchemaDefinition.DefaultBatchSize;
            for (int i = 0; i < itemList.Count; i += batchSize)
            {
                var batch = itemList.Skip(i).Take(batchSize).ToList();
                await _writer.WriteBatchAsync(datasetId, batch, startIndex + i, cancellationToken);

                _logger.LogDebug(
                    "Wrote batch of {BatchSize} items (total progress: {Progress}/{Total})",
                    batch.Count, i + batch.Count, itemList.Count);
            }

            // Update count
            _datasetItemCounts[datasetId] = startIndex + itemList.Count;

            _logger.LogInformation(
                "Successfully added {Count} items to dataset {DatasetId}. Total items: {Total}",
                itemList.Count, datasetId, _datasetItemCounts[datasetId]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add items to dataset {DatasetId}", datasetId);
            throw;
        }
        finally
        {
            datasetLock.Release();
        }
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
        // AddRangeAsync's writer opens each shard with File.Open(path, FileMode.Open,
        // FileAccess.ReadWrite) -- that 3-arg overload defaults to FileShare.None (exclusive),
        // needed for ParquetWriter's backward-seeking footer rewrite (see ParquetItemWriter's
        // own comment on this). Without also taking the same per-dataset lock here, a read
        // landing while a write holds that handle throws "the process cannot access the file
        // because it is being used by another process" -- confirmed live: a real-run gate hit
        // this exact race (GetDatasetItems called right after a background ingestion's final
        // write hadn't yet released the handle) and 500'd. Reads and writes for one dataset now
        // fully serialize; acceptable since writes are already batched/infrequent relative to
        // reads, and the alternative (shared-read access mid footer-rewrite) risks a torn read
        // of a Parquet file, not just a thrown exception.
        var datasetLock = GetDatasetLock(datasetId);
        await datasetLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug(
                "Getting page for dataset {DatasetId} with cursor '{Cursor}' and page size {PageSize}",
                datasetId, cursor ?? "null", pageSize);

            var (items, nextCursor) = await _reader.ReadPageAsync(
                datasetId,
                filter,
                cursor,
                pageSize,
                cancellationToken);

            _logger.LogDebug(
                "Retrieved {Count} items for dataset {DatasetId}. Next cursor: '{NextCursor}'",
                items.Count, datasetId, nextCursor ?? "null");

            return (items, nextCursor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get page for dataset {DatasetId}", datasetId);
            throw;
        }
        finally
        {
            datasetLock.Release();
        }
    }

    /// <summary>
    /// Gets a single item by ID.
    /// </summary>
    public async Task<DatasetItemDto?> GetItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting item {ItemId}", itemId);

            // We need to search across all datasets since we only have item ID
            // For better performance, this could be optimized with an index
            var allDatasetIds = GetAllDatasetIds();

            foreach (var datasetId in allDatasetIds)
            {
                // Same shared-lock reasoning as GetPageAsync: the writer's exclusive file
                // handle during a footer rewrite would otherwise throw here too.
                var datasetLock = GetDatasetLock(datasetId);
                await datasetLock.WaitAsync(cancellationToken);
                DatasetItemDto? item;
                try
                {
                    item = await _reader.ReadItemAsync(datasetId, itemId, cancellationToken);
                }
                finally
                {
                    datasetLock.Release();
                }
                if (item != null)
                {
                    _logger.LogDebug("Found item {ItemId} in dataset {DatasetId}", itemId, datasetId);
                    return item;
                }
            }

            _logger.LogDebug("Item {ItemId} not found", itemId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get item {ItemId}", itemId);
            throw;
        }
    }

    /// <summary>
    /// Updates a single item.
    /// Note: Parquet files are immutable, so this requires rewriting the affected shard(s).
    /// For better performance, use UpdateItemsAsync for bulk updates.
    /// </summary>
    public async Task UpdateItemAsync(
        DatasetItemDto item,
        CancellationToken cancellationToken = default)
    {
        await UpdateItemsAsync(new[] { item }, cancellationToken);
    }

    /// <summary>
    /// Updates multiple items in bulk.
    /// Rewrites affected shards with updated data.
    /// </summary>
    public async Task UpdateItemsAsync(
        IEnumerable<DatasetItemDto> items,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        // Update each dataset under its own lock, rewriting ONLY the shards that
        // actually contain an updated item (instead of reading + rewriting the whole
        // dataset). For a single-shard dataset this touches one shard; for a sharded
        // multi-million-row dataset it leaves untouched shards alone.
        foreach (var datasetGroup in itemList.GroupBy(i => i.DatasetId))
        {
            var datasetId = datasetGroup.Key;
            var updateLookup = datasetGroup.ToDictionary(i => i.Id);

            var datasetLock = GetDatasetLock(datasetId);
            await datasetLock.WaitAsync(cancellationToken);
            try
            {
                int rewrittenShards = 0;

                foreach (var shardIndex in _reader.GetShardIndices(datasetId))
                {
                    var shardItems = await _reader.ReadShardAsync(datasetId, shardIndex, cancellationToken);

                    bool shardChanged = false;
                    for (int i = 0; i < shardItems.Count; i++)
                    {
                        if (updateLookup.TryGetValue(shardItems[i].Id, out var updatedItem))
                        {
                            shardItems[i] = updatedItem with { UpdatedAt = DateTime.UtcNow };
                            shardChanged = true;
                        }
                    }

                    if (shardChanged)
                    {
                        _writer.DeleteShard(datasetId, shardIndex);
                        // Re-anchor to the same shard: every item index stays < ItemsPerShard,
                        // so the whole list is written back into this shard only.
                        long shardStartIndex = (long)shardIndex * ParquetSchemaDefinition.ItemsPerShard;
                        await _writer.WriteBatchAsync(datasetId, shardItems, shardStartIndex, cancellationToken);
                        rewrittenShards++;
                    }
                }

                _logger.LogInformation(
                    "Updated {Count} items in dataset {DatasetId} ({Shards} shard(s) rewritten)",
                    updateLookup.Count, datasetId, rewrittenShards);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update items in dataset {DatasetId}", datasetId);
                throw;
            }
            finally
            {
                datasetLock.Release();
            }
        }
    }

    /// <summary>
    /// Deletes all items for a dataset.
    /// </summary>
    public async Task DeleteByDatasetAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var datasetLock = GetDatasetLock(datasetId);
        await datasetLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting all items for dataset {DatasetId}", datasetId);

            _writer.DeleteDatasetShards(datasetId);
            _datasetItemCounts.TryRemove(datasetId, out _);

            _logger.LogInformation("Successfully deleted all items for dataset {DatasetId}", datasetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete items for dataset {DatasetId}", datasetId);
            throw;
        }
        finally
        {
            datasetLock.Release();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the total count of items in a dataset.
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
        try
        {
            // Fast path for unfiltered counts
            if (filter == null && _datasetItemCounts.TryGetValue(datasetId, out var count))
            {
                return count;
            }

            // Need to count with filter or refresh count. Same shared-lock reasoning as
            // GetPageAsync -- this hits the Parquet files directly, unlike the cached fast
            // path above.
            var datasetLock = GetDatasetLock(datasetId);
            await datasetLock.WaitAsync(cancellationToken);
            long actualCount;
            try
            {
                actualCount = await _reader.CountAsync(datasetId, filter, cancellationToken);
            }
            finally
            {
                datasetLock.Release();
            }

            if (filter == null)
            {
                _datasetItemCounts[datasetId] = actualCount;
            }

            return actualCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get count for dataset {DatasetId}", datasetId);
            throw;
        }
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
        try
        {
            _logger.LogDebug("Computing statistics for dataset {DatasetId}", datasetId);

            // Same shared-lock reasoning as GetPageAsync.
            var datasetLock = GetDatasetLock(datasetId);
            await datasetLock.WaitAsync(cancellationToken);
            List<DatasetItemDto> allItems;
            try
            {
                allItems = await _reader.ReadAllAsync(datasetId, cancellationToken);
            }
            finally
            {
                datasetLock.Release();
            }

            var stats = new Dictionary<string, object>
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

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute statistics for dataset {DatasetId}", datasetId);
            throw;
        }
    }

    /// <summary>
    /// Logs how many datasets have existing Parquet files on disk at startup. Does NOT
    /// pre-populate <see cref="_datasetItemCounts"/> — it used to seed every discovered
    /// dataset with a count of 0 "to be computed on first access", but that 0 IS the cached
    /// value from then on: GetOrInitializeItemCountAsync/GetCountAsync's cache check is a
    /// plain TryGetValue, which succeeds against that placeholder immediately, so the real
    /// count from the Parquet files was never actually read. In production this meant every
    /// dataset with existing data showed 0 items after every service restart, and any
    /// subsequent AddRangeAsync computed new items' starting index from 0 too — silently
    /// colliding with the dataset's existing item indices. Leaving the cache empty here lets
    /// the lazy, on-demand count computation these methods already have work as intended.
    /// </summary>
    private void InitializeItemCounts()
    {
        try
        {
            var allFiles = Directory.GetFiles(_dataDirectory, "dataset_*.parquet");
            var datasetIds = new HashSet<Guid>();

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file);
                if (ParquetSchemaDefinition.TryParseFileName(fileName, out var datasetId, out _))
                {
                    datasetIds.Add(datasetId);
                }
            }

            _logger.LogInformation("Initialized repository with {Count} datasets", datasetIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize item counts from existing files");
        }
    }

    /// <summary>
    /// Gets or initializes the item count for a dataset.
    /// </summary>
    private async Task<long> GetOrInitializeItemCountAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        if (_datasetItemCounts.TryGetValue(datasetId, out var count))
            return count;

        // Need to count existing items. This used to be `task.Wait(); count = task.Result;`
        // — a blocking sync-over-async call made from inside AddRangeAsync while that method
        // holds the per-dataset SemaphoreSlim, risking thread-pool starvation/deadlock under
        // concurrent ingestion load (the blocked thread can't service the async work the
        // count itself depends on).
        count = await _reader.CountAsync(datasetId, cancellationToken: cancellationToken);

        _datasetItemCounts[datasetId] = count;
        return count;
    }

    /// <summary>
    /// Gets all dataset IDs that have data in this repository.
    /// </summary>
    private IEnumerable<Guid> GetAllDatasetIds()
    {
        var allFiles = Directory.GetFiles(_dataDirectory, "dataset_*.parquet");
        var datasetIds = new HashSet<Guid>();

        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileName(file);
            if (ParquetSchemaDefinition.TryParseFileName(fileName, out var datasetId, out _))
            {
                datasetIds.Add(datasetId);
            }
        }

        return datasetIds;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _writer?.Dispose();
        foreach (var datasetLock in _datasetLocks.Values)
        {
            datasetLock.Dispose();
        }
        _datasetLocks.Clear();

        _disposed = true;
    }
}
