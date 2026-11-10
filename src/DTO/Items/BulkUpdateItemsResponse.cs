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

    /// <summary>
    /// Each edited item's fields as the server now holds them, with its version. A client takes
    /// these rather than recomputing the result: a bulk edit applies to the server's current values,
    /// which may include another user's change the client hasn't seen, and recomputing from the
    /// client's copy while adopting the new version let a later edit overwrite that change.
    /// </summary>
    public List<ItemEditResponse> Items { get; init; } = new();
}
