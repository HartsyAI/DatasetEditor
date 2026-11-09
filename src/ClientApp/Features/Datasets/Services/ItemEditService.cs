using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Items;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Items;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Handles item editing operations with API synchronization. Calls go through
/// <see cref="DatasetApiClient"/> so the session cookie is attached (audit U-2).
/// </summary>
public class ItemEditService(DatasetApiClient datasetApiClient, DatasetState datasetState)
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
            DatasetId = item.DatasetId,
            Title = title,
            Description = description,
            Tags = tags,
            IsFavorite = isFavorite
        };

        try
        {
            bool succeeded = await datasetApiClient.UpdateItemAsync(item.Id, request);
            if (!succeeded)
            {
                Logs.Error($"Failed to update item {item.Id}");
                return false;
            }

            // DTOs are immutable, so build the updated copy and push it into the shared state
            // so the card and the detail panel show the saved values without a reload.
            DatasetItemDto updatedItem = item with
            {
                Title = title ?? item.Title,
                Description = description ?? item.Description,
                Tags = tags ?? item.Tags,
                IsFavorite = isFavorite ?? item.IsFavorite,
                UpdatedAt = DateTime.UtcNow
            };
            datasetState.UpdateItem(updatedItem);

            DirtyItemIds.Remove(item.Id.ToString());
            OnDirtyStateChanged?.Invoke();

            Logs.Info($"Item {item.Id} updated successfully");
            return true;
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
        bool? setFavorite = null,
        Guid? datasetId = null)
    {
        BulkUpdateItemsRequest request = new()
        {
            ItemIds = itemIds.Select(Guid.Parse).ToList(),
            DatasetId = datasetId,
            TagsToAdd = tagsToAdd,
            TagsToRemove = tagsToRemove,
            SetFavorite = setFavorite
        };

        try
        {
            int? updatedCount = await datasetApiClient.BulkUpdateItemsAsync(request);
            if (updatedCount is null)
            {
                Logs.Error("Bulk update failed");
                return 0;
            }

            Logs.Info($"Bulk updated {updatedCount} items");

            foreach (string itemId in itemIds)
            {
                DirtyItemIds.Remove(itemId);
            }
            OnDirtyStateChanged?.Invoke();

            return updatedCount.Value;
        }
        catch (Exception ex)
        {
            Logs.Error("Error during bulk update", ex);
            return 0;
        }
    }
}
