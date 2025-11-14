using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Interface for modality-specific providers that handle different data types (Image, Text, Video, etc.)</summary>
public interface IModalityProvider
{
    /// <summary>Gets the modality type this provider handles</summary>
    Modality ModalityType { get; }
    
    /// <summary>Gets human-readable name of this provider</summary>
    string Name { get; }
    
    /// <summary>Gets description of what this provider handles</summary>
    string Description { get; }
    
    /// <summary>Validates if a file is compatible with this modality</summary>
    /// <param name="fileName">File name with extension</param>
    /// <param name="mimeType">Optional MIME type of the file</param>
    /// <returns>True if file is valid for this modality, false otherwise</returns>
    bool ValidateFile(string fileName, string? mimeType = null);
    
    /// <summary>Generates preview data for the item (thumbnail URL, text snippet, etc.)</summary>
    /// <param name="item">The dataset item to generate preview for</param>
    /// <returns>Preview data suitable for UI rendering</returns>
    string GeneratePreview(IDatasetItem item);
    
    /// <summary>Gets supported file extensions for this modality</summary>
    /// <returns>List of file extensions (e.g., ".jpg", ".png", ".mp4")</returns>
    List<string> GetSupportedExtensions();
    
    /// <summary>Gets supported MIME types for this modality</summary>
    /// <returns>List of MIME types (e.g., "image/jpeg", "video/mp4")</returns>
    List<string> GetSupportedMimeTypes();
    
    /// <summary>Gets the default viewer component name for this modality</summary>
    /// <returns>Component name to use for rendering (e.g., "ImageGrid", "TextList")</returns>
    string GetDefaultViewerComponent();
    
    /// <summary>Gets supported operations for this modality (resize, crop, trim, etc.)</summary>
    /// <returns>List of operation names that can be performed on items of this modality</returns>
    List<string> GetSupportedOperations();
    
    /// <summary>Extracts metadata from a file (EXIF for images, duration for video, word count for text, etc.)</summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>Dictionary of extracted metadata</returns>
    Task<Dictionary<string, string>> ExtractMetadataAsync(string filePath);
    
    // TODO: Add support for format conversion capabilities per modality
    // TODO: Add support for quality validation rules per modality
    // TODO: Add support for modality-specific filtering options
}
