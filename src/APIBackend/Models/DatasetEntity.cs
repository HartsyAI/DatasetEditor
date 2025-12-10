using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Models;

public sealed class DatasetEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IngestionStatusDto Status { get; set; } = IngestionStatusDto.Pending;
    public long TotalItems { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? SourceFileName { get; set; }
    public DatasetSourceType SourceType { get; set; } = DatasetSourceType.LocalUpload;
    public string? SourceUri { get; set; }
    public bool IsStreaming { get; set; }
    public string? HuggingFaceRepository { get; set; }
    public string? HuggingFaceConfig { get; set; }
    public string? HuggingFaceSplit { get; set; }
    public string? ErrorMessage { get; set; }
}
