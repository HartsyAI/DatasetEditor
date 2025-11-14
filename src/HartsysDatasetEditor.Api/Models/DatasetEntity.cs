using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Models;

internal sealed class DatasetEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IngestionStatusDto Status { get; set; } = IngestionStatusDto.Pending;
    public long TotalItems { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? SourceFileName { get; set; }
}
