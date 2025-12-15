using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>Repository interface for dataset item operations (Parquet-backed)</summary>
public interface IDatasetItemRepository
{
    /// <summary>Inserts multiple items in bulk to Parquet file</summary>
    Task InsertItemsAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>Gets items for a dataset with pagination from Parquet</summary>
    Task<PagedResult<DatasetItemDto>> GetItemsAsync(Guid datasetId, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets a single item by ID from Parquet</summary>
    Task<DatasetItemDto?> GetItemAsync(Guid datasetId, string itemId, CancellationToken cancellationToken = default);

    /// <summary>Updates a single item in Parquet file</summary>
    Task UpdateItemAsync(Guid datasetId, DatasetItemDto item, CancellationToken cancellationToken = default);

    /// <summary>Bulk updates multiple items in Parquet file</summary>
    Task BulkUpdateItemsAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>Deletes an item from Parquet file</summary>
    Task DeleteItemAsync(Guid datasetId, string itemId, CancellationToken cancellationToken = default);

    /// <summary>Gets total count of items in a dataset's Parquet file</summary>
    Task<long> GetItemCountAsync(Guid datasetId, CancellationToken cancellationToken = default);

    /// <summary>Searches items by title, description, or tags</summary>
    Task<PagedResult<DatasetItemDto>> SearchItemsAsync(Guid datasetId, string query, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets items by tag</summary>
    Task<PagedResult<DatasetItemDto>> GetItemsByTagAsync(Guid datasetId, string tag, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets favorite items</summary>
    Task<PagedResult<DatasetItemDto>> GetFavoriteItemsAsync(Guid datasetId, int offset, int limit, CancellationToken cancellationToken = default);
}
