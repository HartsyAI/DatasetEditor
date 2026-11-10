namespace DatasetStudio.DTO.Items;

/// <summary>Why a bulk edit skipped an item (<see cref="BulkSkippedItem.Reason"/>).</summary>
public static class BulkSkipReasons
{
    /// <summary>
    /// No such item, or it is in a dataset the caller can't see (reported the same way, so a
    /// private dataset's items aren't revealed).
    /// </summary>
    public const string NotFound = "not-found";

    /// <summary>The caller can see the item's dataset but may not edit it.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>
    /// A tag change on a HuggingFace streaming item whose tags the server doesn't hold: they were
    /// never edited and the request didn't send the tags the client shows for it
    /// (<see cref="BulkUpdateItemsRequest.CurrentTags"/>).
    /// </summary>
    public const string TagsUnknown = "tags-unknown";
}
