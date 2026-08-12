using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.AspNetCore.Mvc;

namespace DatasetStudio.APIBackend.Endpoints;

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
            .ProducesProblem(404)
            .RequireAuthorization();

        // Bulk update items
        group.MapPatch("/bulk", BulkUpdateItems)
            .WithName("BulkUpdateItems")
            .Produces<int>()
            .ProducesProblem(400)
            .RequireAuthorization();
    }

    public static async Task<IResult> UpdateItem(
        Guid itemId,
        [FromBody] UpdateItemRequest request,
        IDatasetItemRepository itemRepository,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetPermissionService permissionService,
        HttpContext httpContext)
    {
        DatasetItemDto? item = await itemRepository.GetItemAsync(itemId);

        if (item == null)
        {
            return Results.NotFound(new { message = $"Item {itemId} not found" });
        }

        // Item edits are a write on the owning dataset -- resolve it and apply the same
        // owner/admin/granted-write rule as DatasetEndpoints' mutation routes.
        DatasetDto? owningDataset = await datasetRepository.GetByIdAsync(item.DatasetId);
        if (owningDataset is null)
        {
            return Results.Forbid();
        }
        // Same existence-leak guard as DatasetEndpoints' mutation routes: if the caller can't
        // even read the owning dataset (private, not theirs, no grant), the item itself must
        // read as not-found, not "exists but forbidden".
        if (!await permissionService.CanReadAsync(owningDataset, httpContext.User))
        {
            return Results.NotFound(new { message = $"Item {itemId} not found" });
        }
        if (!await permissionService.CanWriteAsync(owningDataset, httpContext.User))
        {
            return Results.Forbid();
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
        IDatasetItemRepository itemRepository,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetPermissionService permissionService,
        HttpContext httpContext)
    {
        if (!request.ItemIds.Any())
        {
            return Results.BadRequest(new { message = "No item IDs provided" });
        }

        List<DatasetItemDto> itemsToUpdate = new();
        // A bulk request is expected to stay within one open dataset view, but nothing enforces
        // that at the API layer -- cache the write-permission result per DatasetId rather than
        // assuming a single dataset (or re-checking the same one on every item).
        Dictionary<Guid, bool> writePermissionByDataset = new();

        foreach (Guid itemId in request.ItemIds)
        {
            DatasetItemDto? item = await itemRepository.GetItemAsync(itemId);
            if (item == null)
                continue;

            if (!writePermissionByDataset.TryGetValue(item.DatasetId, out bool canWrite))
            {
                DatasetDto? owningDataset = await datasetRepository.GetByIdAsync(item.DatasetId);
                canWrite = owningDataset is not null && await permissionService.CanWriteAsync(owningDataset, httpContext.User);
                writePermissionByDataset[item.DatasetId] = canWrite;
            }
            if (!canWrite)
            {
                // Same tolerant "skip and keep going" behavior as an item that doesn't exist --
                // a client-observable partial-success count rather than failing the whole batch
                // because one item belongs to a dataset the caller can't write to.
                continue;
            }

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

