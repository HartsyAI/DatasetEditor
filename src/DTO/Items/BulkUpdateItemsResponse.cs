namespace DatasetStudio.DTO.Items;

/// <summary>
/// Body of <c>PATCH /api/items/bulk</c>: how many items were edited and which were not, and why.
/// The endpoint used to return only the count, so a client could not tell a denied item from a
/// missing one, or know which items were left unchanged (audit E-1).
/// </summary>
public sealed record BulkUpdateItemsResponse
{
    /// <summary>How many items were edited.</summary>
    public int UpdatedCount { get; init; }

    /// <summary>The requested items that were not edited.</summary>
    public List<BulkSkippedItem> Skipped { get; init; } = new();

    /// <summary>
    /// The new version of each edited item, so a client can keep editing it with <c>If-Match</c>
    /// without its next single edit being refused as stale (audit E-4).
    /// </summary>
    public Dictionary<Guid, string> Versions { get; init; } = new();
}
