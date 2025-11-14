namespace HartsysDatasetEditor.Contracts.Common;

/// <summary>Represents a cursor-based page request.</summary>
public sealed record PageRequest
{
    /// <summary>Maximum number of items to return. Defaults to 100.</summary>
    public int PageSize { get; init; } = 100;

    /// <summary>Opaque cursor pointing to the next page. Null indicates start of collection.</summary>
    public string? Cursor { get; init; }
}
