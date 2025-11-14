namespace HartsysDatasetEditor.Contracts.Common;

/// <summary>Represents filter criteria sent from clients to query dataset items.</summary>
public sealed record FilterRequest
{
    public string? SearchQuery { get; init; }
    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public bool? FavoritesOnly { get; init; }
    public int? MinWidth { get; init; }
    public int? MaxWidth { get; init; }
    public int? MinHeight { get; init; }
    public int? MaxHeight { get; init; }
    public double? MinAspectRatio { get; init; }
    public double? MaxAspectRatio { get; init; }
    public IReadOnlyCollection<string> Formats { get; init; } = Array.Empty<string>();
    public string? Photographer { get; init; }
    public string? Location { get; init; }
}
