using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Contracts.Items;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
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
        IDatasetItem? item = itemRepository.GetItem(itemId);
        
        if (item == null)
        {
            return Results.NotFound(new { message = $"Item {itemId} not found" });
        }
        
        if (item is ImageItem imageItem)
        {
            // Update fields if provided
            if (request.Title != null)
            {
                imageItem.Title = request.Title;
            }
            
            if (request.Description != null)
            {
                imageItem.Description = request.Description;
            }
            
            if (request.Tags != null)
            {
                imageItem.Tags = request.Tags;
            }
            
            if (request.IsFavorite.HasValue)
            {
                imageItem.IsFavorite = request.IsFavorite.Value;
            }
            
            if (request.Metadata != null)
            {
                foreach (KeyValuePair<string, string> kvp in request.Metadata)
                {
                    imageItem.Metadata[kvp.Key] = kvp.Value;
                }
            }
            
            imageItem.UpdatedAt = DateTime.UtcNow;
            
            // Save to database
            itemRepository.UpdateItem(imageItem);
            
            Logs.Info($"Updated item {itemId}: Title={request.Title}, Tags={request.Tags?.Count ?? 0}");
            
            // Return updated item
            DatasetItemDto dto = MapToDto(imageItem);
            return Results.Ok(dto);
        }
        
        return Results.BadRequest(new { message = "Item type not supported for editing" });
    }
    
    public static async Task<IResult> BulkUpdateItems(
        [FromBody] BulkUpdateItemsRequest request,
        IDatasetItemRepository itemRepository)
    {
        if (!request.ItemIds.Any())
        {
            return Results.BadRequest(new { message = "No item IDs provided" });
        }
        
        List<IDatasetItem> itemsToUpdate = new();
        
        foreach (Guid itemId in request.ItemIds)
        {
            IDatasetItem? item = itemRepository.GetItem(itemId);
            if (item == null)
                continue;
            
            if (item is ImageItem imageItem)
            {
                // Add tags
                if (request.TagsToAdd != null)
                {
                    foreach (string tag in request.TagsToAdd)
                    {
                        if (!imageItem.Tags.Contains(tag))
                        {
                            imageItem.Tags.Add(tag);
                        }
                    }
                }
                
                // Remove tags
                if (request.TagsToRemove != null)
                {
                    foreach (string tag in request.TagsToRemove)
                    {
                        imageItem.Tags.Remove(tag);
                    }
                }
                
                // Set favorite
                if (request.SetFavorite.HasValue)
                {
                    imageItem.IsFavorite = request.SetFavorite.Value;
                }
                
                // Add metadata
                if (request.MetadataToAdd != null)
                {
                    foreach (KeyValuePair<string, string> kvp in request.MetadataToAdd)
                    {
                        imageItem.Metadata[kvp.Key] = kvp.Value;
                    }
                }
                
                imageItem.UpdatedAt = DateTime.UtcNow;
                itemsToUpdate.Add(imageItem);
            }
        }
        
        // Bulk update in database
        itemRepository.BulkUpdateItems(itemsToUpdate);
        
        Logs.Info($"Bulk updated {itemsToUpdate.Count} items");
        
        return Results.Ok(new { updatedCount = itemsToUpdate.Count });
    }
    
    public static DatasetItemDto MapToDto(ImageItem item)
    {
        return new DatasetItemDto
        {
            Id = Guid.Parse(item.Id),
            DatasetId = Guid.Parse(item.DatasetId),
            Title = item.Title,
            Description = item.Description,
            ImageUrl = item.ImageUrl,
            ThumbnailUrl = item.ThumbnailUrl,
            Tags = item.Tags,
            IsFavorite = item.IsFavorite,
            Metadata = item.Metadata,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }
}
