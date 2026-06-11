namespace DatasetStudio.DTO.Datasets;

/// <summary>Detailed dataset information returned by the API.</summary>
public sealed record DatasetDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IngestionStatusDto Status { get; init; } = IngestionStatusDto.Pending;
    public long TotalItems { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? SourceFileName { get; init; }
    public DatasetSourceType SourceType { get; init; } = DatasetSourceType.LocalUpload;
    public string? SourceUri { get; init; }
    public bool IsStreaming { get; init; }
    public string? HuggingFaceRepository { get; init; }
    public string? HuggingFaceConfig { get; init; }
    public string? HuggingFaceSplit { get; init; }
    public string? ErrorMessage { get; init; }
}
