using Microsoft.JSInterop;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.Utilities;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Services.Interop;

/// <summary>C# wrapper for IndexedDB JavaScript cache</summary>
public class IndexedDbInterop(IJSRuntime jsRuntime)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    /// <summary>Initializes the IndexedDB database</summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("indexedDbCache.initialize");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to initialize IndexedDB", ex);
            return false;
        }
    }
    
    /// <summary>Saves multiple items to cache</summary>
    public async Task<bool> SaveItemsAsync(List<DatasetItemDto> items)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("indexedDbCache.saveItems", items);
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to save items to IndexedDB", ex);
            return false;
        }
    }
    
    /// <summary>Gets items for a specific dataset with pagination</summary>
    public async Task<List<DatasetItemDto>> GetItemsAsync(string datasetId, int page, int pageSize)
    {
        try
        {
            List<DatasetItemDto>? items = await _jsRuntime.InvokeAsync<List<DatasetItemDto>>(
                "indexedDbCache.getItems", datasetId, page, pageSize);
            
            return items ?? new List<DatasetItemDto>();
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to get items from IndexedDB", ex);
            return new List<DatasetItemDto>();
        }
    }
    
    /// <summary>Saves a page of items</summary>
    public async Task<bool> SavePageAsync(string datasetId, int page, List<DatasetItemDto> items)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>(
                "indexedDbCache.savePage", datasetId, page, items);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to save page {page} to IndexedDB", ex);
            return false;
        }
    }
    
    /// <summary>Gets a cached page</summary>
    public async Task<CachedPage?> GetPageAsync(string datasetId, int page)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<CachedPage?>(
                "indexedDbCache.getPage", datasetId, page);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get page {page} from IndexedDB", ex);
            return null;
        }
    }
    
    /// <summary>Clears all cached data for a specific dataset</summary>
    public async Task<bool> ClearDatasetAsync(string datasetId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>(
                "indexedDbCache.clearDataset", datasetId);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to clear dataset {datasetId} from IndexedDB", ex);
            return false;
        }
    }
    
    /// <summary>Saves dataset metadata</summary>
    public async Task<bool> SaveDatasetAsync(DatasetSummaryDto dataset)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>(
                "indexedDbCache.saveDataset", dataset);
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to save dataset to IndexedDB", ex);
            return false;
        }
    }
    
    /// <summary>Gets dataset metadata</summary>
    public async Task<DatasetSummaryDto?> GetDatasetAsync(string datasetId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<DatasetSummaryDto?>(
                "indexedDbCache.getDataset", datasetId);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get dataset {datasetId} from IndexedDB", ex);
            return null;
        }
    }
    
    /// <summary>Sets a cache value</summary>
    public async Task<bool> SetCacheValueAsync(string key, object value, int expiresInMinutes = 60)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>(
                "indexedDbCache.setCacheValue", key, value, expiresInMinutes);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to set cache value for key: {key}", ex);
            return false;
        }
    }
    
    /// <summary>Gets a cache value</summary>
    public async Task<T?> GetCacheValueAsync<T>(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<T?>("indexedDbCache.getCacheValue", key);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get cache value for key: {key}", ex);
            return default;
        }
    }
    
    /// <summary>Gets cache statistics</summary>
    public async Task<CacheStats?> GetCacheStatsAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<CacheStats?>("indexedDbCache.getCacheStats");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to get cache stats", ex);
            return null;
        }
    }
    
    /// <summary>Clears all cached data</summary>
    public async Task<bool> ClearAllAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("indexedDbCache.clearAll");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to clear all cache", ex);
            return false;
        }
    }
}

/// <summary>Represents a cached page</summary>
public class CachedPage
{
    public string DatasetId { get; set; } = string.Empty;
    public int Page { get; set; }
    public List<DatasetItemDto> Items { get; set; } = new();
    public string CachedAt { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

/// <summary>Cache statistics</summary>
public class CacheStats
{
    public int Items { get; set; }
    public int Pages { get; set; }
    public int Datasets { get; set; }
}
