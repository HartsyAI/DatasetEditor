namespace DatasetStudio.DTO.Items;

/// <summary>Request to update a single dataset item</summary>
public class UpdateItemRequest
{
    public Guid ItemId { get; set; }
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

    /// <summary>Tags to add to all items</summary>
    public List<string>? TagsToAdd { get; set; }

    /// <summary>Tags to remove from all items</summary>
    public List<string>? TagsToRemove { get; set; }

    /// <summary>Set all items as favorite/unfavorite</summary>
    public bool? SetFavorite { get; set; }

    /// <summary>Metadata to add/update on all items</summary>
    public Dictionary<string, string>? MetadataToAdd { get; set; }
}
