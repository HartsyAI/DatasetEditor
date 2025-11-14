using HartsysDatasetEditor.Core.Enums;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Interface for all dataset items providing modality-agnostic contract</summary>
public interface IDatasetItem
{
    /// <summary>Unique identifier for this item</summary>
    string Id { get; set; }
    
    /// <summary>Reference to the parent dataset ID</summary>
    string DatasetId { get; set; }
    
    /// <summary>The modality type of this item</summary>
    Modality Modality { get; }
    
    /// <summary>Path or URL to the source file/resource</summary>
    string SourcePath { get; set; }
    
    /// <summary>Optional display name or title</summary>
    string Title { get; set; }
    
    /// <summary>Optional description or caption</summary>
    string Description { get; set; }
    
    /// <summary>When this item was added to the dataset</summary>
    DateTime CreatedAt { get; set; }
    
    /// <summary>When this item was last modified</summary>
    DateTime UpdatedAt { get; set; }
    
    /// <summary>Tags associated with this item</summary>
    List<string> Tags { get; set; }
    
    /// <summary>Additional metadata specific to this item</summary>
    Dictionary<string, string> Metadata { get; set; }
    
    /// <summary>Whether this item is marked as favorite</summary>
    bool IsFavorite { get; set; }
    
    /// <summary>Gets preview data suitable for rendering (URL, snippet, etc.)</summary>
    string GetPreviewData();
}
