using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Items;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.Utilities;
using System.Net.Http.Json;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>Handles item editing operations with API synchronization</summary>
public class ItemEditService(HttpClient httpClient, DatasetState datasetState)
{
    public HashSet<string> DirtyItemIds { get; } = new();
    
    public event Action? OnDirtyStateChanged;
    
    /// <summary>Updates a single item field (title, description, etc.)</summary>
    public async Task<bool> UpdateItemAsync(
        ImageItem item,
        string? title = null,
        string? description = null,
        List<string>? tags = null,
        bool? isFavorite = null)
    {
        UpdateItemRequest request = new()
        {
            ItemId = Guid.Parse(item.Id),
            Title = title,
            Description = description,
            Tags = tags,
            IsFavorite = isFavorite
        };
        
        try
        {
            HttpResponseMessage response = await httpClient.PatchAsJsonAsync(
                $"/api/items/{item.Id}",
                request);
            
            if (response.IsSuccessStatusCode)
            {
                // Update local item
                if (title != null) item.Title = title;
                if (description != null) item.Description = description;
                if (tags != null) item.Tags = tags;
                if (isFavorite.HasValue) item.IsFavorite = isFavorite.Value;
                
                item.UpdatedAt = DateTime.UtcNow;
                
                // Update in state
                datasetState.UpdateItem(item);
                
                // Mark as clean (saved)
                DirtyItemIds.Remove(item.Id);
                OnDirtyStateChanged?.Invoke();
                
                Logs.Info($"Item {item.Id} updated successfully");
                return true;
            }
            else
            {
                Logs.Error($"Failed to update item {item.Id}: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error updating item {item.Id}", ex);
            return false;
        }
    }
    
    /// <summary>Marks an item as dirty (has unsaved changes)</summary>
    public void MarkDirty(string itemId)
    {
        DirtyItemIds.Add(itemId);
        OnDirtyStateChanged?.Invoke();
    }
    
    /// <summary>Adds a tag to an item</summary>
    public async Task<bool> AddTagAsync(ImageItem item, string tag)
    {
        if (item.Tags.Contains(tag))
            return true;
        
        List<string> newTags = new(item.Tags) { tag };
        return await UpdateItemAsync(item, tags: newTags);
    }
    
    /// <summary>Removes a tag from an item</summary>
    public async Task<bool> RemoveTagAsync(ImageItem item, string tag)
    {
        if (!item.Tags.Contains(tag))
            return true;
        
        List<string> newTags = item.Tags.Where(t => t != tag).ToList();
        return await UpdateItemAsync(item, tags: newTags);
    }
    
    /// <summary>Toggles favorite status</summary>
    public async Task<bool> ToggleFavoriteAsync(ImageItem item)
    {
        return await UpdateItemAsync(item, isFavorite: !item.IsFavorite);
    }
    
    /// <summary>Bulk updates multiple items</summary>
    public async Task<int> BulkUpdateAsync(
        List<string> itemIds,
        List<string>? tagsToAdd = null,
        List<string>? tagsToRemove = null,
        bool? setFavorite = null)
    {
        BulkUpdateItemsRequest request = new()
        {
            ItemIds = itemIds.Select(Guid.Parse).ToList(),
            TagsToAdd = tagsToAdd,
            TagsToRemove = tagsToRemove,
            SetFavorite = setFavorite
        };
        
        try
        {
            HttpResponseMessage response = await httpClient.PatchAsJsonAsync(
                "/api/items/bulk",
                request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BulkUpdateResponse>();
                int updatedCount = result?.UpdatedCount ?? 0;
                
                Logs.Info($"Bulk updated {updatedCount} items");
                
                // Refresh affected items from state
                foreach (string itemId in itemIds)
                {
                    DirtyItemIds.Remove(itemId);
                }
                OnDirtyStateChanged?.Invoke();
                
                return updatedCount;
            }
            else
            {
                Logs.Error($"Bulk update failed: {response.StatusCode}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Logs.Error("Error during bulk update", ex);
            return 0;
        }
    }
    
    private record BulkUpdateResponse(int UpdatedCount);
}
