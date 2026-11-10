using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Items;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Handles item editing operations with API synchronization. Calls go through
/// <see cref="DatasetApiClient"/> so the session cookie is attached (audit U-2). Every edit sends
/// the item's version as <c>If-Match</c>, so an edit made from a stale copy is refused rather than
/// overwriting someone else's change (audit E-4). Streaming (HuggingFace, S3) items are edited the
/// same way; the server keeps their edits in an overlay (audit E-3).
/// </summary>
public class ItemEditService(DatasetApiClient datasetApiClient, DatasetState datasetState)
{
    /// <summary>What to tell the user when an edit was refused because the item changed (412).</summary>
    public const string ConflictMessage = "This item was changed by someone else. It has been reloaded; make your change again.";

    public HashSet<string> DirtyItemIds { get; } = new();

    public event Action? OnDirtyStateChanged;

    /// <summary>
    /// Updates a single item's fields. On success the item in <see cref="DatasetState"/> takes the
    /// new values and version. On a conflict it is replaced by the server's current values, so the
    /// user sees what changed (callers show <see cref="ConflictMessage"/>).
    /// </summary>
    public async Task<ItemUpdateOutcome> UpdateItemAsync(
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
            ItemUpdateResult result = await datasetApiClient.UpdateItemAsync(item.Id, request, item.Version);
            switch (result.Outcome)
            {
                case ItemUpdateOutcome.Saved:
                    // DTOs are immutable, so build the updated copy and push it into the shared
                    // state so the card and the detail panel show the saved values without a reload.
                    DatasetItemDto updatedItem = item with
                    {
                        Title = title ?? item.Title,
                        Description = description ?? item.Description,
                        Tags = tags ?? item.Tags,
                        IsFavorite = isFavorite ?? item.IsFavorite,
                        UpdatedAt = DateTime.UtcNow
                    };
                    datasetState.UpdateItem(ApplyServerView(updatedItem, result.Item));

                    DirtyItemIds.Remove(item.Id.ToString());
                    OnDirtyStateChanged?.Invoke();
                    Logs.Info($"Item {item.Id} updated successfully");
                    return ItemUpdateOutcome.Saved;

                case ItemUpdateOutcome.Conflict:
                    // Reload the item from the server's reply: its current fields and version.
                    datasetState.UpdateItem(ApplyServerView(item, result.Item));
                    Logs.Warning($"Item {item.Id} changed since it was loaded; edit refused and item reloaded");
                    return ItemUpdateOutcome.Conflict;

                default:
                    Logs.Error($"Failed to update item {item.Id}");
                    return ItemUpdateOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error updating item {item.Id}", ex);
            return ItemUpdateOutcome.Failed;
        }
    }

    /// <summary>
    /// The item with the server's view applied: every field the server holds replaces the local
    /// value, and the version is the server's. For a streaming item the server holds only the
    /// edited fields (the rest come from the source), so the others keep their local values.
    /// </summary>
    public static DatasetItemDto ApplyServerView(DatasetItemDto item, ItemEditResponse? server)
    {
        if (server is null)
        {
            return item;
        }

        Dictionary<string, string> metadata = item.Metadata;
        if (server.Metadata is not null)
        {
            metadata = new Dictionary<string, string>(item.Metadata);
            foreach (KeyValuePair<string, string> entry in server.Metadata)
            {
                metadata[entry.Key] = entry.Value;
            }
        }

        return item with
        {
            Title = server.Title ?? item.Title,
            Description = server.Description ?? item.Description,
            Tags = server.Tags ?? item.Tags,
            IsFavorite = server.IsFavorite ?? item.IsFavorite,
            Metadata = metadata,
            Version = string.IsNullOrEmpty(server.Version) ? item.Version : server.Version
        };
    }

    /// <summary>Marks an item as dirty (has unsaved changes)</summary>
    public void MarkDirty(string itemId)
    {
        DirtyItemIds.Add(itemId);
        OnDirtyStateChanged?.Invoke();
    }

    /// <summary>Adds a tag to an item</summary>
    public async Task<ItemUpdateOutcome> AddTagAsync(DatasetItemDto item, string tag)
    {
        if (item.Tags.Contains(tag))
            return ItemUpdateOutcome.Saved;

        List<string> newTags = new(item.Tags) { tag };
        return await UpdateItemAsync(item, tags: newTags);
    }

    /// <summary>Removes a tag from an item</summary>
    public async Task<ItemUpdateOutcome> RemoveTagAsync(DatasetItemDto item, string tag)
    {
        if (!item.Tags.Contains(tag))
            return ItemUpdateOutcome.Saved;

        List<string> newTags = item.Tags.Where(t => t != tag).ToList();
        return await UpdateItemAsync(item, tags: newTags);
    }

    /// <summary>Toggles favorite status</summary>
    public async Task<ItemUpdateOutcome> ToggleFavoriteAsync(DatasetItemDto item)
    {
        return await UpdateItemAsync(item, isFavorite: !item.IsFavorite);
    }

    /// <summary>
    /// Bulk-edits items of one dataset. The server reports the items it skipped and why (audit
    /// E-1); <see cref="BulkEditResult.Summary"/> says so in words for the user. Tag changes send
    /// the tags each item shows, which the server needs for a HuggingFace streaming item whose tags
    /// were never edited (their source tags aren't stored anywhere it can read).
    /// </summary>
    public async Task<BulkEditResult> BulkUpdateAsync(
        IReadOnlyList<DatasetItemDto> items,
        List<string>? tagsToAdd = null,
        List<string>? tagsToRemove = null,
        bool? setFavorite = null)
    {
        bool changesTags = (tagsToAdd?.Count ?? 0) > 0 || (tagsToRemove?.Count ?? 0) > 0;
        List<Guid> datasetIds = items.Select(i => i.DatasetId).Distinct().ToList();
        BulkUpdateItemsRequest request = new()
        {
            ItemIds = items.Select(i => i.Id).ToList(),
            DatasetId = datasetIds.Count == 1 ? datasetIds[0] : null,
            TagsToAdd = tagsToAdd,
            TagsToRemove = tagsToRemove,
            SetFavorite = setFavorite,
            CurrentTags = changesTags ? items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Tags) : null
        };

        try
        {
            BulkUpdateItemsResponse? response = await datasetApiClient.BulkUpdateItemsAsync(request);
            if (response is null)
            {
                Logs.Error("Bulk update failed");
                return BulkEditResult.Failed(items.Count);
            }

            HashSet<Guid> skipped = response.Skipped.Select(s => s.ItemId).ToHashSet();
            foreach (DatasetItemDto item in items.Where(i => !skipped.Contains(i.Id)))
            {
                DatasetItemDto edited = item with
                {
                    Tags = changesTags ? EditTags(item.Tags, tagsToAdd, tagsToRemove) : item.Tags,
                    IsFavorite = setFavorite ?? item.IsFavorite,
                    // The new version, so the next single edit of this item isn't refused as stale.
                    Version = response.Versions.TryGetValue(item.Id, out string? version) ? version : item.Version
                };
                datasetState.UpdateItem(edited);
                DirtyItemIds.Remove(item.Id.ToString());
            }
            OnDirtyStateChanged?.Invoke();

            BulkEditResult result = BulkEditResult.From(response, items.Count);
            Logs.Info(result.Summary);
            return result;
        }
        catch (Exception ex)
        {
            Logs.Error("Error during bulk update", ex);
            return BulkEditResult.Failed(items.Count);
        }
    }

    private static List<string> EditTags(List<string> tags, List<string>? add, List<string>? remove)
    {
        List<string> result = new(tags);
        foreach (string tag in add ?? new List<string>())
        {
            if (!result.Contains(tag))
            {
                result.Add(tag);
            }
        }
        foreach (string tag in remove ?? new List<string>())
        {
            result.Remove(tag);
        }
        return result;
    }
}
