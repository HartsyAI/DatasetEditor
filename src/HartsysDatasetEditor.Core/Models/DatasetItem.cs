using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;

namespace HartsysDatasetEditor.Core.Models;

/// <summary>Base class for all dataset items (images, text, video, etc.). Provides common properties and modality-agnostic structure.</summary>
public abstract class DatasetItem : IDatasetItem
{
    /// <summary>Unique identifier for this item within the dataset</summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>Reference to the parent dataset ID</summary>
    public string DatasetId { get; set; } = string.Empty;
    
    /// <summary>The modality type of this item</summary>
    public abstract Modality Modality { get; }
    
    /// <summary>Path or URL to the source file/resource</summary>
    public string SourcePath { get; set; } = string.Empty;
    
    /// <summary>Optional display name or title</summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>Optional description or caption</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>When this item was added to the dataset</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>When this item was last modified</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>Tags associated with this item for filtering and organization</summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>Additional metadata specific to this item stored as key-value pairs</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    /// <summary>Whether this item is marked as favorite/starred</summary>
    public bool IsFavorite { get; set; }
    
    /// <summary>Gets preview data suitable for rendering (thumbnail URL, text snippet, etc.)</summary>
    public abstract string GetPreviewData();
    
    // TODO: Add support for annotations when implementing annotation features
    // TODO: Add support for captions when implementing captioning features
    // TODO: Add support for quality scores/ratings
    // TODO: Add support for item relationships (duplicates, similar items, etc.)
}
