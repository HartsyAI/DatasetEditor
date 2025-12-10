using DatasetStudio.Core.Enumerations;

namespace DatasetStudio.Core.DomainModels.Datasets;

/// <summary>Represents a complete dataset with metadata and items</summary>
public class Dataset
{
    /// <summary>Unique identifier for the dataset</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name of the dataset</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the dataset contents</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The modality type of this dataset (Image, Text, Video, etc.)</summary>
    public Modality Modality { get; set; } = Modality.Unknown;

    /// <summary>The format type of the source data (TSV, COCO, YOLO, etc.)</summary>
    public DatasetFormat Format { get; set; } = DatasetFormat.Unknown;

    /// <summary>Total number of items in the dataset</summary>
    public int TotalItems { get; set; }

    /// <summary>When the dataset was created in the application</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the dataset was last modified</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Source file name or URL where dataset was loaded from</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Additional metadata as key-value pairs for extensibility</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Tags for organization and filtering</summary>
    public List<string> Tags { get; set; } = new();

    // TODO: Add support for versioning when implementing dataset history
    // TODO: Add support for collaborative features (owner, shared users, permissions)
    // TODO: Add statistics (total size, avg dimensions, format breakdown)
}
