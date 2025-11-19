using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Contracts.Items;
using HartsysDatasetEditor.Core.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace HartsysDatasetEditor.Api.Endpoints;

/// <summary>API endpoints for editing dataset items</summary>
public static class ItemEditEndpoints
{
    public static void MapItemEditEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/items").WithTags("Items");

        // Update single item
        group.MapPatch("/{itemId:guid}", UpdateItem)
            .WithName("UpdateItem")
            .Produces<DatasetItemDto>()
            .ProducesProblem(404);

        // Bulk update items
        group.MapPatch("/bulk", BulkUpdateItems)
            .WithName("BulkUpdateItems")
            .Produces<int>()
            .ProducesProblem(400);
    }

    public static async Task<IResult> UpdateItem(
        Guid itemId,
        [FromBody] UpdateItemRequest request,
        IDatasetItemRepository itemRepository)
    {
        DatasetItemDto? item = await itemRepository.GetItemAsync(itemId);

        if (item == null)
        {
            return Results.NotFound(new { message = $"Item {itemId} not found" });
        }

        // Update fields if provided
        if (request.Title != null)
        {
            item = item with { Title = request.Title };
        }

        if (request.Description != null)
        {
            item = item with { Description = request.Description };
        }

        if (request.Tags != null)
        {
            item = item with { Tags = request.Tags };
        }

        if (request.IsFavorite.HasValue)
        {
            item = item with { IsFavorite = request.IsFavorite.Value };
        }

        if (request.Metadata != null)
        {
            Dictionary<string, string> updatedMetadata = item.Metadata != null
                ? new Dictionary<string, string>(item.Metadata)
                : new Dictionary<string, string>();

            foreach (KeyValuePair<string, string> kvp in request.Metadata)
            {
                updatedMetadata[kvp.Key] = kvp.Value;
            }

            item = item with { Metadata = updatedMetadata };
        }

        item = item with { UpdatedAt = DateTime.UtcNow };

        // Save to database
        await itemRepository.UpdateItemAsync(item);

        Logs.Info($"Updated item {itemId}: Title={request.Title}, Tags={request.Tags?.Count ?? 0}");

        return Results.Ok(item);
    }

    public static async Task<IResult> BulkUpdateItems(
        [FromBody] BulkUpdateItemsRequest request,
        IDatasetItemRepository itemRepository)
    {
        if (!request.ItemIds.Any())
        {
            return Results.BadRequest(new { message = "No item IDs provided" });
        }

        List<DatasetItemDto> itemsToUpdate = new();

        foreach (Guid itemId in request.ItemIds)
        {
            DatasetItemDto? item = await itemRepository.GetItemAsync(itemId);
            if (item == null)
                continue;

            // Add tags
            if (request.TagsToAdd != null && request.TagsToAdd.Any())
            {
                List<string> updatedTags = item.Tags?.ToList() ?? new List<string>();
                foreach (string tag in request.TagsToAdd)
                {
                    if (!updatedTags.Contains(tag))
                    {
                        updatedTags.Add(tag);
                    }
                }
                item = item with { Tags = updatedTags };
            }

            // Remove tags
            if (request.TagsToRemove != null && request.TagsToRemove.Any())
            {
                List<string> updatedTags = item.Tags?.ToList() ?? new List<string>();
                foreach (string tag in request.TagsToRemove)
                {
                    updatedTags.Remove(tag);
                }
                item = item with { Tags = updatedTags };
            }

            // Set favorite
            if (request.SetFavorite.HasValue)
            {
                item = item with { IsFavorite = request.SetFavorite.Value };
            }

            // Add metadata
            if (request.MetadataToAdd != null && request.MetadataToAdd.Any())
            {
                Dictionary<string, string> updatedMetadata = item.Metadata != null
                    ? new Dictionary<string, string>(item.Metadata)
                    : new Dictionary<string, string>();

                foreach (KeyValuePair<string, string> kvp in request.MetadataToAdd)
                {
                    updatedMetadata[kvp.Key] = kvp.Value;
                }

                item = item with { Metadata = updatedMetadata };
            }

            item = item with { UpdatedAt = DateTime.UtcNow };
            itemsToUpdate.Add(item);
        }

        // Bulk update in database
        await itemRepository.UpdateItemsAsync(itemsToUpdate);

        Logs.Info($"Bulk updated {itemsToUpdate.Count} items");

        return Results.Ok(new { updatedCount = itemsToUpdate.Count });
    }
}
