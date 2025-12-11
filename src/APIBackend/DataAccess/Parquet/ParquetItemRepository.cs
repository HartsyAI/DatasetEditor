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
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<Guid, long> _datasetItemCounts = new();
    private bool _disposed;

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

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Get current count to determine starting index
            long startIndex = GetOrInitializeItemCount(datasetId);

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
            _writeLock.Release();
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
                var item = await _reader.ReadItemAsync(datasetId, itemId, cancellationToken);
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

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Group items by dataset
            var itemsByDataset = itemList.GroupBy(i => i.DatasetId);

            foreach (var datasetGroup in itemsByDataset)
            {
                var datasetId = datasetGroup.Key;
                var datasetItems = datasetGroup.ToList();

                _logger.LogInformation(
                    "Updating {Count} items in dataset {DatasetId}",
                    datasetItems.Count, datasetId);

                // Read all items from the dataset
                var allItems = await _reader.ReadAllAsync(datasetId, cancellationToken);

                // Create a lookup for updates
                var updateLookup = datasetItems.ToDictionary(i => i.Id);

                // Apply updates
                for (int i = 0; i < allItems.Count; i++)
                {
                    if (updateLookup.TryGetValue(allItems[i].Id, out var updatedItem))
                    {
                        allItems[i] = updatedItem with { UpdatedAt = DateTime.UtcNow };
                    }
                }

                // Delete old shards
                _writer.DeleteDatasetShards(datasetId);

                // Write updated data
                await _writer.WriteBatchAsync(datasetId, allItems, 0, cancellationToken);

                _logger.LogInformation(
                    "Successfully updated {Count} items in dataset {DatasetId}",
                    datasetItems.Count, datasetId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update items");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Deletes all items for a dataset.
    /// </summary>
    public async Task DeleteByDatasetAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting all items for dataset {DatasetId}", datasetId);

            _writer.DeleteDatasetShards(datasetId);
            _datasetItemCounts.Remove(datasetId);

            _logger.LogInformation("Successfully deleted all items for dataset {DatasetId}", datasetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete items for dataset {DatasetId}", datasetId);
            throw;
        }
        finally
        {
            _writeLock.Release();
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

            // Need to count with filter or refresh count
            var actualCount = await _reader.CountAsync(datasetId, filter, cancellationToken);

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

            var allItems = await _reader.ReadAllAsync(datasetId, cancellationToken);

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
    /// Initializes item counts by scanning existing Parquet files.
    /// </summary>
    private void InitializeItemCounts()
    {
        try
        {
            var allFiles = Directory.GetFiles(_dataDirectory, "dataset_*.parquet");

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file);
                if (ParquetSchemaDefinition.TryParseFileName(fileName, out var datasetId, out _))
                {
                    if (!_datasetItemCounts.ContainsKey(datasetId))
                    {
                        // Count will be computed on first access
                        _datasetItemCounts[datasetId] = 0;
                    }
                }
            }

            _logger.LogInformation("Initialized repository with {Count} datasets", _datasetItemCounts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize item counts from existing files");
        }
    }

    /// <summary>
    /// Gets or initializes the item count for a dataset.
    /// </summary>
    private long GetOrInitializeItemCount(Guid datasetId)
    {
        if (_datasetItemCounts.TryGetValue(datasetId, out var count))
            return count;

        // Need to count existing items
        var task = _reader.CountAsync(datasetId);
        task.Wait();
        count = task.Result;

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
        _writeLock?.Dispose();

        _disposed = true;
    }
}
