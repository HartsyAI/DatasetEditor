namespace DatasetStudio.DTO.Items;

/// <summary>Request to update a single dataset item</summary>
public class UpdateItemRequest
{
    /// <summary>Ignored: the item is the one in the route (audit E-3 noted the redundancy).</summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// The item's dataset. Required for a streaming (HuggingFace or S3) item: its id is derived
    /// from its source and can't be looked up on its own. For a local item it is optional, but
    /// when sent the server looks the item up in that dataset only, instead of searching every
    /// dataset's id indexes (audit P-2).
    /// </summary>
    public Guid? DatasetId { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public bool? IsFavorite { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>Request to bulk update multiple items</summary>
public class BulkUpdateItemsRequest
{
    public List<Guid> ItemIds { get; set; } = new();

    /// <summary>
    /// The items' dataset, when they all share one (see <see cref="UpdateItemRequest.DatasetId"/>).
    /// Required for streaming items.
    /// </summary>
    public Guid? DatasetId { get; set; }

    /// <summary>Tags to add to all items</summary>
    public List<string>? TagsToAdd { get; set; }

    /// <summary>Tags to remove from all items</summary>
    public List<string>? TagsToRemove { get; set; }

    /// <summary>Set all items as favorite/unfavorite</summary>
    public bool? SetFavorite { get; set; }

    /// <summary>Metadata to add/update on all items</summary>
    public Dictionary<string, string>? MetadataToAdd { get; set; }

    /// <summary>
    /// The tags the client shows for each item. Only used for a tag change on a HuggingFace
    /// streaming item whose tags were never edited: the server stores an edited item's whole tag
    /// list, and can't read the row's own tags back from an item id, so it starts from these.
    /// </summary>
    public Dictionary<Guid, List<string>>? CurrentTags { get; set; }
}
