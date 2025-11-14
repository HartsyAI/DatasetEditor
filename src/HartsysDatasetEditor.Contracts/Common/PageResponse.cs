namespace HartsysDatasetEditor.Contracts.Common;

/// <summary>Standardized paginated response with cursor-based navigation.</summary>
public sealed record PageResponse<T>
{
    /// <summary>Collection of items returned for the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Opaque cursor representing the next page. Null if no further results.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Total items available (if known). Optional for streaming backends.</summary>
    public long? TotalCount { get; init; }
}
