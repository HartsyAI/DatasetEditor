using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>
/// Repository interface for dataset item ingestion (Parquet-backed). Only what
/// DatasetIngestionService actually needs — this interface previously declared
/// GetItemsAsync/SearchItemsAsync/GetItemsByTagAsync/GetFavoriteItemsAsync/DeleteItemAsync/
/// BulkUpdateItemsAsync/GetItemCountAsync/GetItemAsync too, but none of them had any call
/// site anywhere in the codebase, and each one that took a page (offset/limit) implemented
/// it by fetching the ENTIRE dataset via GetPageAsync(..., pageSize: int.MaxValue, ...) and
/// slicing in memory — exactly the "flat memory" problem the app's own architecture doc
/// warns against. Real item reads/writes/edits go through
/// Services.DatasetManagement.IDatasetItemRepository (the cursor-paged, ParquetItemRepository-
/// singleton-backed interface used by ItemEditEndpoints and GetDatasetItems), not this one.
/// </summary>
public interface IDatasetItemRepository
{
    /// <summary>Inserts multiple items in bulk to Parquet file</summary>
    Task InsertItemsAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>Gets the true total item count for a dataset (not just the most recent ingestion batch).</summary>
    Task<long> GetCountAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
