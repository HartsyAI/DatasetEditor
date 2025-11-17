using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;
using LiteDB;

namespace HartsysDatasetEditor.Api.Repositories;

/// <summary>LiteDB implementation of dataset item repository</summary>
public class LiteDbItemRepository : IDatasetItemRepository
{
    private readonly LiteDatabase _database;
    private readonly string _collectionName = "items";
    
    public LiteDbItemRepository(string databasePath)
    {
        _database = new LiteDatabase(databasePath);
        
        // Create indexes for common queries
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        collection.EnsureIndex(x => x.DatasetId);
        collection.EnsureIndex(x => x.Title);
        collection.EnsureIndex(x => x.Tags);
        collection.EnsureIndex(x => x.IsFavorite);
        collection.EnsureIndex(x => x.CreatedAt);
        
        Logs.Info($"LiteDB item repository initialized with indexes");
    }
    
    public void InsertItems(Guid datasetId, IEnumerable<IDatasetItem> items)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        List<ImageItem> imageItems = items.Cast<ImageItem>().ToList();
        
        // Set dataset ID and timestamps
        foreach (ImageItem item in imageItems)
        {
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
            }
            item.DatasetId = datasetId.ToString();
            item.CreatedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
        }
        
        collection.InsertBulk(imageItems);
        
        Logs.Info($"Inserted {imageItems.Count} items for dataset {datasetId}");
    }
    
    public PagedResult<IDatasetItem> GetItems(Guid datasetId, int page, int pageSize)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        string datasetIdString = datasetId.ToString();
        long total = collection.Count(x => x.DatasetId == datasetIdString);
        
        List<ImageItem> items = collection.Query()
            .Where(x => x.DatasetId == datasetIdString)
            .OrderBy(x => x.CreatedAt)
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
        
        return new PagedResult<IDatasetItem>
        {
            Items = items.Cast<IDatasetItem>().ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
    
    public IDatasetItem? GetItem(Guid itemId)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        return collection.FindById(itemId.ToString());
    }
    
    public void UpdateItem(IDatasetItem item)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        if (item is ImageItem imageItem)
        {
            imageItem.UpdatedAt = DateTime.UtcNow;
            collection.Update(imageItem);
            Logs.Info($"Updated item: {item.Id}");
        }
    }
    
    public void BulkUpdateItems(IEnumerable<IDatasetItem> items)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        List<ImageItem> imageItems = items.Cast<ImageItem>().ToList();
        DateTime now = DateTime.UtcNow;
        
        foreach (ImageItem item in imageItems)
        {
            item.UpdatedAt = now;
            collection.Update(item);
        }
        
        Logs.Info($"Bulk updated {imageItems.Count} items");
    }
    
    public void DeleteItem(Guid itemId)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        collection.Delete(itemId.ToString());
        Logs.Info($"Deleted item: {itemId}");
    }
    
    public long GetItemCount(Guid datasetId)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        return collection.Count(x => x.DatasetId == datasetId.ToString());
    }
    
    public PagedResult<IDatasetItem> SearchItems(Guid datasetId, string query, int page, int pageSize)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        string datasetIdString = datasetId.ToString();
        string lowerQuery = query.ToLowerInvariant();
        
        ILiteQueryable<ImageItem> queryable = collection.Query()
            .Where(x => x.DatasetId == datasetIdString && 
                       (x.Title.ToLower().Contains(lowerQuery) || 
                        x.Description.ToLower().Contains(lowerQuery)));
        
        long total = queryable.Count();
        
        List<ImageItem> items = queryable
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
        
        return new PagedResult<IDatasetItem>
        {
            Items = items.Cast<IDatasetItem>().ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
    
    public PagedResult<IDatasetItem> GetItemsByTag(Guid datasetId, string tag, int page, int pageSize)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        string datasetIdString = datasetId.ToString();
        
        ILiteQueryable<ImageItem> queryable = collection.Query()
            .Where(x => x.DatasetId == datasetIdString && x.Tags.Contains(tag));
        
        long total = queryable.Count();
        
        List<ImageItem> items = queryable
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
        
        return new PagedResult<IDatasetItem>
        {
            Items = items.Cast<IDatasetItem>().ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
    
    public PagedResult<IDatasetItem> GetFavoriteItems(Guid datasetId, int page, int pageSize)
    {
        ILiteCollection<ImageItem> collection = _database.GetCollection<ImageItem>(_collectionName);
        
        string datasetIdString = datasetId.ToString();
        
        ILiteQueryable<ImageItem> queryable = collection.Query()
            .Where(x => x.DatasetId == datasetIdString && x.IsFavorite);
        
        long total = queryable.Count();
        
        List<ImageItem> items = queryable
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
        
        return new PagedResult<IDatasetItem>
        {
            Items = items.Cast<IDatasetItem>().ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
}
