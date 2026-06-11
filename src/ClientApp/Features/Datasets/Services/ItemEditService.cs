using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Items;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Items;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using System.Net.Http.Json;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>Handles item editing operations with API synchronization</summary>
public class ItemEditService(HttpClient httpClient, DatasetState datasetState)
{
    public HashSet<string> DirtyItemIds { get; } = new();
    
    public event Action? OnDirtyStateChanged;
    
    /// <summary>Updates a single item field (title, description, etc.)</summary>
    public async Task<bool> UpdateItemAsync(
        DatasetItemDto item,
        string? title = null,
        string? description = null,
        List<string>? tags = null,
        bool? isFavorite = null)
    {
        UpdateItemRequest request = new()
        {
            ItemId = item.Id,
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
                // Create updated item using 'with' expression (DTO is immutable)
                DatasetItemDto updatedItem = item with
                {
                    Title = title ?? item.Title,
                    Description = description ?? item.Description,
                    Tags = tags ?? item.Tags,
                    IsFavorite = isFavorite ?? item.IsFavorite,
                    UpdatedAt = DateTime.UtcNow
                };

                // Update in state
                // TODO: DatasetState.UpdateItem needs to accept DatasetItemDto instead of IDatasetItem
                // For now, we'll skip this update - the item will be refreshed on next load
                // datasetState.UpdateItem(updatedItem);

                // Mark as clean (saved)
                DirtyItemIds.Remove(item.Id.ToString());
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
    public async Task<bool> AddTagAsync(DatasetItemDto item, string tag)
    {
        if (item.Tags.Contains(tag))
            return true;
        
        List<string> newTags = new(item.Tags) { tag };
        return await UpdateItemAsync(item, tags: newTags);
    }
    
    /// <summary>Removes a tag from an item</summary>
    public async Task<bool> RemoveTagAsync(DatasetItemDto item, string tag)
    {
        if (!item.Tags.Contains(tag))
            return true;
        
        List<string> newTags = item.Tags.Where(t => t != tag).ToList();
        return await UpdateItemAsync(item, tags: newTags);
    }
    
    /// <summary>Toggles favorite status</summary>
    public async Task<bool> ToggleFavoriteAsync(DatasetItemDto item)
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
