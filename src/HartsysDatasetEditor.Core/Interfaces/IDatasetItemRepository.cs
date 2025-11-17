using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Repository interface for dataset item operations</summary>
public interface IDatasetItemRepository
{
    /// <summary>Inserts multiple items in bulk</summary>
    void InsertItems(Guid datasetId, IEnumerable<IDatasetItem> items);
    
    /// <summary>Gets items for a dataset with pagination</summary>
    PagedResult<IDatasetItem> GetItems(Guid datasetId, int page, int pageSize);
    
    /// <summary>Gets a single item by ID</summary>
    IDatasetItem? GetItem(Guid itemId);
    
    /// <summary>Updates a single item</summary>
    void UpdateItem(IDatasetItem item);
    
    /// <summary>Bulk updates multiple items</summary>
    void BulkUpdateItems(IEnumerable<IDatasetItem> items);
    
    /// <summary>Deletes an item</summary>
    void DeleteItem(Guid itemId);
    
    /// <summary>Gets total count of items in a dataset</summary>
    long GetItemCount(Guid datasetId);
    
    /// <summary>Searches items by title, description, or tags</summary>
    PagedResult<IDatasetItem> SearchItems(Guid datasetId, string query, int page, int pageSize);
    
    /// <summary>Gets items by tag</summary>
    PagedResult<IDatasetItem> GetItemsByTag(Guid datasetId, string tag, int page, int pageSize);
    
    /// <summary>Gets favorite items</summary>
    PagedResult<IDatasetItem> GetFavoriteItems(Guid datasetId, int page, int pageSize);
}
