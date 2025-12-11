using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Example usage of the Parquet storage system.
/// This class demonstrates common patterns and best practices.
/// </summary>
public static class ParquetRepositoryExample
{
    /// <summary>
    /// Example: Adding millions of items to a dataset.
    /// </summary>
    public static async Task BulkImportExample(
        ParquetItemRepository repository,
        Guid datasetId,
        IEnumerable<DatasetItemDto> items,
        ILogger logger)
    {
        var itemList = items.ToList();
        logger.LogInformation("Starting bulk import of {Count} items", itemList.Count);

        // Process in chunks to avoid memory issues
        const int chunkSize = 100_000;
        int processed = 0;

        for (int i = 0; i < itemList.Count; i += chunkSize)
        {
            var chunk = itemList.Skip(i).Take(chunkSize);

            await repository.AddRangeAsync(datasetId, chunk);

            processed += chunkSize;
            logger.LogInformation("Progress: {Processed}/{Total}", processed, itemList.Count);
        }

        logger.LogInformation("Bulk import completed");
    }

    /// <summary>
    /// Example: Paginating through a large dataset.
    /// </summary>
    public static async Task PaginationExample(
        ParquetItemRepository repository,
        Guid datasetId,
        ILogger logger)
    {
        string? cursor = null;
        const int pageSize = 100;
        int totalProcessed = 0;

        do
        {
            var (items, nextCursor) = await repository.GetPageAsync(
                datasetId,
                filter: null,
                cursor: cursor,
                pageSize: pageSize
            );

            // Process items
            foreach (var item in items)
            {
                logger.LogDebug("Processing item: {Title}", item.Title);
                // Do something with the item
            }

            totalProcessed += items.Count;
            cursor = nextCursor;

            logger.LogInformation("Processed {Total} items so far", totalProcessed);
        }
        while (cursor != null);

        logger.LogInformation("Pagination complete. Total items: {Total}", totalProcessed);
    }

    /// <summary>
    /// Example: Searching and filtering items.
    /// </summary>
    public static async Task SearchExample(
        ParquetItemRepository repository,
        Guid datasetId,
        ILogger logger)
    {
        // Example 1: Search by text
        var searchFilter = new FilterRequest
        {
            SearchQuery = "landscape"
        };

        var (searchResults, _) = await repository.GetPageAsync(
            datasetId,
            filter: searchFilter,
            cursor: null,
            pageSize: 50
        );

        logger.LogInformation("Found {Count} items matching 'landscape'", searchResults.Count);

        // Example 2: Filter by dimensions
        var dimensionFilter = new FilterRequest
        {
            MinWidth = 1920,
            MinHeight = 1080,
            MaxAspectRatio = 2.0  // No ultra-wide images
        };

        var (dimensionResults, _) = await repository.GetPageAsync(
            datasetId,
            filter: dimensionFilter,
            cursor: null,
            pageSize: 50
        );

        logger.LogInformation("Found {Count} HD images", dimensionResults.Count);

        // Example 3: Filter by tags
        var tagFilter = new FilterRequest
        {
            Tags = new[] { "landscape", "nature" }
        };

        var (tagResults, _) = await repository.GetPageAsync(
            datasetId,
            filter: tagFilter,
            cursor: null,
            pageSize: 50
        );

        logger.LogInformation("Found {Count} items with tags", tagResults.Count);

        // Example 4: Complex filter
        var complexFilter = new FilterRequest
        {
            SearchQuery = "sunset",
            Tags = new[] { "landscape" },
            MinWidth = 1920,
            FavoritesOnly = true,
            DateFrom = DateTime.UtcNow.AddMonths(-6)
        };

        var (complexResults, _) = await repository.GetPageAsync(
            datasetId,
            filter: complexFilter,
            cursor: null,
            pageSize: 50
        );

        logger.LogInformation("Found {Count} items with complex filter", complexResults.Count);
    }

    /// <summary>
    /// Example: Updating items efficiently.
    /// </summary>
    public static async Task UpdateExample(
        ParquetItemRepository repository,
        Guid datasetId,
        ILogger logger)
    {
        // Get items to update
        var (items, _) = await repository.GetPageAsync(
            datasetId,
            filter: new FilterRequest { SearchQuery = "old_value" },
            cursor: null,
            pageSize: 1000
        );

        logger.LogInformation("Updating {Count} items", items.Count);

        // Modify items
        var updatedItems = items.Select(item => item with
        {
            Title = item.Title.Replace("old_value", "new_value"),
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        // Bulk update (more efficient than one-by-one)
        await repository.UpdateItemsAsync(updatedItems);

        logger.LogInformation("Update complete");
    }

    /// <summary>
    /// Example: Computing statistics.
    /// </summary>
    public static async Task StatisticsExample(
        ParquetItemRepository repository,
        Guid datasetId,
        ILogger logger)
    {
        // Get comprehensive statistics
        var stats = await repository.GetStatisticsAsync(datasetId);

        logger.LogInformation("Dataset Statistics:");
        logger.LogInformation("  Total Items: {Total}", stats["total_items"]);
        logger.LogInformation("  Favorites: {Favorites}", stats["favorite_count"]);
        logger.LogInformation("  Avg Width: {Width:F2}px", stats["avg_width"]);
        logger.LogInformation("  Avg Height: {Height:F2}px", stats["avg_height"]);
        logger.LogInformation("  Width Range: {Min}-{Max}px", stats["min_width"], stats["max_width"]);
        logger.LogInformation("  Height Range: {Min}-{Max}px", stats["min_height"], stats["max_height"]);

        if (stats.TryGetValue("tag_counts", out var tagCountsObj) &&
            tagCountsObj is Dictionary<string, int> tagCounts)
        {
            logger.LogInformation("  Top Tags:");
            foreach (var (tag, count) in tagCounts.OrderByDescending(x => x.Value).Take(10))
            {
                logger.LogInformation("    {Tag}: {Count}", tag, count);
            }
        }
    }

    /// <summary>
    /// Example: Working with low-level reader for advanced scenarios.
    /// </summary>
    public static async Task LowLevelReaderExample(
        string dataDirectory,
        Guid datasetId,
        ILogger logger)
    {
        var reader = new ParquetItemReader(dataDirectory);

        // Count with filter (uses parallel shard reading)
        var count = await reader.CountAsync(
            datasetId,
            filter: new FilterRequest { FavoritesOnly = true }
        );

        logger.LogInformation("Favorite items count: {Count}", count);

        // Find specific item by ID (searches all shards in parallel)
        var itemId = Guid.NewGuid(); // Replace with actual ID
        var item = await reader.ReadItemAsync(datasetId, itemId);

        if (item != null)
        {
            logger.LogInformation("Found item: {Title}", item.Title);
        }
        else
        {
            logger.LogWarning("Item not found: {ItemId}", itemId);
        }
    }

    /// <summary>
    /// Example: Working with low-level writer for custom scenarios.
    /// </summary>
    public static async Task LowLevelWriterExample(
        string dataDirectory,
        Guid datasetId,
        List<DatasetItemDto> items,
        ILogger logger)
    {
        using var writer = new ParquetItemWriter(dataDirectory);

        // Write in custom batches
        const int batchSize = 50_000;
        long startIndex = 0;

        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize).ToList();

            await writer.WriteBatchAsync(
                datasetId,
                batch,
                startIndex + i
            );

            logger.LogInformation(
                "Wrote batch {Batch}/{Total}",
                (i / batchSize) + 1,
                (items.Count + batchSize - 1) / batchSize
            );
        }

        // Ensure all data is flushed to disk
        await writer.FlushAsync();

        logger.LogInformation("All data written successfully");
    }

    /// <summary>
    /// Example: Migrating from another storage system.
    /// </summary>
    public static async Task MigrationExample(
        IEnumerable<DatasetItemDto> sourceItems,
        ParquetItemRepository targetRepository,
        Guid targetDatasetId,
        ILogger logger)
    {
        logger.LogInformation("Starting migration");

        var items = sourceItems.ToList();
        const int batchSize = 100_000;
        int migrated = 0;

        // Process in batches to manage memory
        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize).ToList();

            // Transform items if needed
            var transformedBatch = batch.Select(item => item with
            {
                // Ensure all required fields are set
                CreatedAt = item.CreatedAt == default ? DateTime.UtcNow : item.CreatedAt,
                UpdatedAt = item.UpdatedAt == default ? DateTime.UtcNow : item.UpdatedAt,
                DatasetId = targetDatasetId
            }).ToList();

            await targetRepository.AddRangeAsync(
                targetDatasetId,
                transformedBatch
            );

            migrated += batch.Count;
            logger.LogInformation(
                "Migration progress: {Migrated}/{Total} ({Percentage:F2}%)",
                migrated,
                items.Count,
                (migrated * 100.0 / items.Count)
            );
        }

        // Verify migration
        var finalCount = await targetRepository.GetCountAsync(targetDatasetId);
        logger.LogInformation(
            "Migration complete. Expected: {Expected}, Actual: {Actual}",
            items.Count,
            finalCount
        );

        if (finalCount != items.Count)
        {
            logger.LogWarning("Migration count mismatch!");
        }
    }
}
