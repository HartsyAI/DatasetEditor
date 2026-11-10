namespace DatasetStudio.DTO.Items;

/// <summary>An item a bulk edit left unchanged, with the reason (one of <see cref="BulkSkipReasons"/>).</summary>
public sealed record BulkSkippedItem
{
    /// <summary>The item.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Why it was skipped: a <see cref="BulkSkipReasons"/> value.</summary>
    public string Reason { get; init; } = string.Empty;
}
