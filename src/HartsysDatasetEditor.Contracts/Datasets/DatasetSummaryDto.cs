namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>Lightweight projection returned to clients when listing datasets.</summary>
public sealed record DatasetSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IngestionStatusDto Status { get; init; } = IngestionStatusDto.Pending;
    public long TotalItems { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string Format { get; init; } = string.Empty;
    public string Modality { get; init; } = string.Empty;
}
