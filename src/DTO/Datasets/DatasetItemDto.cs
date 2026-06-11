namespace DatasetStudio.DTO.Datasets;

/// <summary>Dataset item projection returned in list queries.</summary>
public sealed record DatasetItemDto
{
    public Guid Id { get; init; }
    public Guid DatasetId { get; init; }
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? ImageUrl { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public List<string> Tags { get; init; } = new();
    public bool IsFavorite { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
