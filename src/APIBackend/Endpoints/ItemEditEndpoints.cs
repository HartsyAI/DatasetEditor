using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Core.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>API endpoints for editing dataset items</summary>
public static class ItemEditEndpoints
{
    /// <summary>The 412 message: the edit was made from a copy older than the item.</summary>
    internal const string StaleVersionMessage =
        "This item was changed by someone else since you loaded it. Reload it and make your change again.";

    public static void MapItemEditEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/items").WithTags("Items");

        // Update single item. Honours If-Match (412 on a stale version) and returns the new
        // version as an ETag (audit E-4).
        group.MapPatch("/{itemId:guid}", UpdateItem)
            .WithName("UpdateItem")
            .Produces<ItemEditResponse>()
            .Produces<ItemEditResponse>(StatusCodes.Status412PreconditionFailed)
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
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // The dataset comes from the request when sent: then the item is looked up in that
        // dataset's id indexes only (audit P-2). Without it, every dataset is searched.
        Guid datasetId;
        if (request.DatasetId is Guid requestedDatasetId)
        {
            datasetId = requestedDatasetId;
        }
        else
        {
            DatasetItemDto? found = await itemRepository.GetItemAsync(itemId, cancellationToken);
            if (found is null)
            {
                return ItemNotFound(itemId);
            }
            datasetId = found.DatasetId;
        }

        // Item edits are a write on the owning dataset -- apply the same owner/admin/granted-write
        // rule as DatasetEndpoints' mutation routes. Same existence-leak guard as those routes too:
        // if the caller can't even read the dataset (private, not theirs, no grant), the item
        // itself must read as not-found, not "exists but forbidden".
        DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null || !await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return ItemNotFound(itemId);
        }
        if (!await permissionService.CanWriteAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.Forbid();
        }

        string? ifMatch = httpContext.Request.Headers.IfMatch.Count > 0 ? httpContext.Request.Headers.IfMatch.ToString() : null;

        // The patch is applied to the item as it stands inside the dataset's lock, with the
        // If-Match check in the same lock, so neither a concurrent edit of another field nor one
        // of the same field made since the caller loaded the item is lost (audit E-4).
        ItemEditResult result = await itemRepository.EditItemAsync(
            datasetId,
            itemId,
            current => ItemVersions.IfMatchAllows(ifMatch, current.Version ?? ItemVersions.ForLocal(current.UpdatedAt)),
            current => ApplyPatch(current, request),
            cancellationToken);

        switch (result.Status)
        {
            case ItemEditStatus.NotFound:
                return ItemNotFound(itemId);
            case ItemEditStatus.PreconditionFailed:
                return StaleVersion(httpContext, ToResponse(result.Item!) with { Message = StaleVersionMessage });
        }

        DatasetItemDto item = result.Item!;

        // Who changed which item and which fields, never the values: titles, captions and tags are
        // user content and used to be written to the log (audit E-5), while the editor wasn't.
        logger.LogInformation(
            "[Audit] User {UserId} edited item {ItemId} in dataset {DatasetId} (fields: {Fields})",
            httpContext.User.GetUserId(), itemId, item.DatasetId, ChangedFields(request));

        ItemEditResponse response = ToResponse(item);
        httpContext.Response.Headers.ETag = ItemVersions.ToETag(response.Version);
        return Results.Ok(response);
    }

    public static async Task<IResult> BulkUpdateItems(
        [FromBody] BulkUpdateItemsRequest request,
        IDatasetItemRepository itemRepository,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!request.ItemIds.Any())
        {
            return Results.BadRequest(new { message = "No item IDs provided" });
        }

        // Group the ids by dataset: the request's when sent, otherwise each item's own (found by
        // searching every dataset). A bulk request is expected to stay within one open dataset
        // view, but nothing enforces that at the API layer.
        Dictionary<Guid, List<Guid>> idsByDataset = new();
        foreach (Guid itemId in request.ItemIds.Distinct())
        {
            Guid? datasetId = request.DatasetId;
            if (datasetId is null)
            {
                DatasetItemDto? found = await itemRepository.GetItemAsync(itemId, cancellationToken);
                datasetId = found?.DatasetId;
            }
            if (datasetId is Guid id)
            {
                if (!idsByDataset.TryGetValue(id, out List<Guid>? ids))
                {
                    ids = new List<Guid>();
                    idsByDataset[id] = ids;
                }
                ids.Add(itemId);
            }
        }

        List<DatasetItemDto> updated = new();
        foreach ((Guid datasetId, List<Guid> ids) in idsByDataset)
        {
            DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
            if (dataset is null || !await permissionService.CanWriteAsync(dataset, httpContext.User, cancellationToken))
            {
                // Same tolerant "skip and keep going" behavior as an item that doesn't exist --
                // a partial-success count rather than failing the whole batch because one item
                // belongs to a dataset the caller can't write to.
                continue;
            }

            // Applied to each item as it stands, under the dataset's lock (audit E-4): tags are
            // added to the current list, not to a copy read before a concurrent edit.
            updated.AddRange(await itemRepository.EditItemsAsync(datasetId, ids, current => ApplyBulk(current, request), cancellationToken));
        }

        // Who changed how many items in which datasets (audit E-5); skipped items aren't counted.
        logger.LogInformation(
            "[Audit] User {UserId} bulk-edited {ItemCount} of {RequestedCount} items in datasets {DatasetIds}",
            httpContext.User.GetUserId(), updated.Count, request.ItemIds.Count,
            string.Join(",", updated.Select(i => i.DatasetId).Distinct()));

        return Results.Ok(new { updatedCount = updated.Count });
    }

    /// <summary>Applies a single-item edit request to the item as it stands.</summary>
    internal static DatasetItemDto ApplyPatch(DatasetItemDto item, UpdateItemRequest request)
    {
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
            item = item with { Metadata = MergeMetadata(item.Metadata, request.Metadata) };
        }
        return item;
    }

    /// <summary>Applies a bulk edit request to one item as it stands.</summary>
    internal static DatasetItemDto ApplyBulk(DatasetItemDto item, BulkUpdateItemsRequest request)
    {
        if ((request.TagsToAdd?.Count ?? 0) > 0 || (request.TagsToRemove?.Count ?? 0) > 0)
        {
            item = item with { Tags = EditTags(item.Tags, request.TagsToAdd, request.TagsToRemove) };
        }
        if (request.SetFavorite.HasValue)
        {
            item = item with { IsFavorite = request.SetFavorite.Value };
        }
        if (request.MetadataToAdd != null && request.MetadataToAdd.Count > 0)
        {
            item = item with { Metadata = MergeMetadata(item.Metadata, request.MetadataToAdd) };
        }
        return item;
    }

    /// <summary>The tag list with <paramref name="add"/> appended (once each) and <paramref name="remove"/> taken out.</summary>
    internal static List<string> EditTags(IEnumerable<string>? tags, IEnumerable<string>? add, IEnumerable<string>? remove)
    {
        List<string> result = tags?.ToList() ?? new List<string>();
        foreach (string tag in add ?? Enumerable.Empty<string>())
        {
            if (!result.Contains(tag))
            {
                result.Add(tag);
            }
        }
        foreach (string tag in remove ?? Enumerable.Empty<string>())
        {
            result.Remove(tag);
        }
        return result;
    }

    private static Dictionary<string, string> MergeMetadata(Dictionary<string, string>? current, Dictionary<string, string> changes)
    {
        Dictionary<string, string> merged = current != null ? new Dictionary<string, string>(current) : new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> kvp in changes)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }

    /// <summary>A local item's editable fields and version, as the edit response.</summary>
    private static ItemEditResponse ToResponse(DatasetItemDto item)
    {
        return new ItemEditResponse
        {
            ItemId = item.Id,
            DatasetId = item.DatasetId,
            Version = item.Version ?? ItemVersions.ForLocal(item.UpdatedAt),
            Title = item.Title,
            Description = item.Description,
            Tags = item.Tags,
            IsFavorite = item.IsFavorite,
            Metadata = item.Metadata
        };
    }

    private static IResult ItemNotFound(Guid itemId)
    {
        return Results.NotFound(new { message = $"Item {itemId} not found" });
    }

    /// <summary>412 with the item as it stands, so the client can show the current values.</summary>
    private static IResult StaleVersion(HttpContext httpContext, ItemEditResponse current)
    {
        httpContext.Response.Headers.ETag = ItemVersions.ToETag(current.Version);
        return Results.Json(current, statusCode: StatusCodes.Status412PreconditionFailed);
    }

    /// <summary>The names of the fields an edit request sets, for the audit log (never their values).</summary>
    internal static string ChangedFields(UpdateItemRequest request)
    {
        List<string> fields = new();
        if (request.Title != null)
        {
            fields.Add("title");
        }
        if (request.Description != null)
        {
            fields.Add("description");
        }
        if (request.Tags != null)
        {
            fields.Add("tags");
        }
        if (request.IsFavorite.HasValue)
        {
            fields.Add("favorite");
        }
        if (request.Metadata != null)
        {
            fields.Add("metadata");
        }
        return fields.Count == 0 ? "none" : string.Join(",", fields);
    }
}
