using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Models;

public sealed class DatasetDiskMetadata
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DatasetSourceType SourceType { get; set; } = DatasetSourceType.LocalUpload;
    public string? SourceUri { get; set; }
    public string? SourceFileName { get; set; }
    public string? PrimaryFile { get; set; }
    public List<string> AuxiliaryFiles { get; set; } = new();
}
