namespace DatasetStudio.DTO.Items;

/// <summary>
/// Body of <c>PATCH /api/items/{id}</c>, on success (200) and on a version conflict (412). It
/// carries the item's version (also sent as the <c>ETag</c> header) and its editable fields as the
/// server now holds them. A null field is one the server doesn't hold: for a streaming
/// (HuggingFace or S3) item, a field that was never edited, whose value comes from the source.
/// A client applies the non-null fields over its copy of the item.
/// </summary>
public sealed record ItemEditResponse
{
    /// <summary>The edited item.</summary>
    public Guid ItemId { get; init; }

    /// <summary>The item's dataset.</summary>
    public Guid DatasetId { get; init; }

    /// <summary>The item's version after the edit, or its current version on a conflict.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>The title, when the server holds it.</summary>
    public string? Title { get; init; }

    /// <summary>The description, when the server holds it.</summary>
    public string? Description { get; init; }

    /// <summary>The tags, when the server holds them.</summary>
    public List<string>? Tags { get; init; }

    /// <summary>The favourite flag, when the server holds it.</summary>
    public bool? IsFavorite { get; init; }

    /// <summary>
    /// Metadata the server holds: all of it for a local item, only the edited keys for a
    /// streaming one (they are merged over the source's metadata).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>Why the edit was refused (412 only).</summary>
    public string? Message { get; init; }
}
