using HartsysDatasetEditor.Core.Enums;

namespace HartsysDatasetEditor.Core.Models;

/// <summary>Represents an image item in a dataset with image-specific properties</summary>
public class ImageItem : DatasetItem
{
    /// <summary>Gets the modality type (always Image for this class)</summary>
    public override Modality Modality => Modality.Image;
    
    /// <summary>Direct URL to the full-size image</summary>
    public string ImageUrl { get; set; } = string.Empty;
    
    /// <summary>Optional thumbnail URL (smaller version for grid display)</summary>
    public string ThumbnailUrl { get; set; } = string.Empty;
    
    /// <summary>Image width in pixels</summary>
    public int Width { get; set; }
    
    /// <summary>Image height in pixels</summary>
    public int Height { get; set; }
    
    /// <summary>Aspect ratio (width / height)</summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
    
    /// <summary>File format (JPEG, PNG, WebP, etc.)</summary>
    public string Format { get; set; } = string.Empty;
    
    /// <summary>File size in bytes</summary>
    public long FileSizeBytes { get; set; }
    
    /// <summary>Color space (RGB, CMYK, Grayscale, etc.)</summary>
    public string ColorSpace { get; set; } = "RGB";
    
    /// <summary>Photographer or creator name (from Unsplash and similar datasets)</summary>
    public string Photographer { get; set; } = string.Empty;
    
    /// <summary>Photographer username or handle</summary>
    public string PhotographerUsername { get; set; } = string.Empty;
    
    /// <summary>Photographer profile URL</summary>
    public string PhotographerUrl { get; set; } = string.Empty;
    
    /// <summary>Average color of the image in hex format (#RRGGBB)</summary>
    public string AverageColor { get; set; } = string.Empty;
    
    /// <summary>Dominant colors in the image</summary>
    public List<string> DominantColors { get; set; } = new();
    
    /// <summary>Number of views (if available from source)</summary>
    public int Views { get; set; }
    
    /// <summary>Number of downloads (if available from source)</summary>
    public int Downloads { get; set; }
    
    /// <summary>Number of likes (if available from source)</summary>
    public int Likes { get; set; }
    
    /// <summary>GPS latitude if available</summary>
    public double? Latitude { get; set; }
    
    /// <summary>GPS longitude if available</summary>
    public double? Longitude { get; set; }
    
    /// <summary>Location name or description</summary>
    public string Location { get; set; } = string.Empty;
    
    /// <summary>EXIF data from the image file</summary>
    public Dictionary<string, string> ExifData { get; set; } = new();
    
    /// <summary>Gets the preview data for rendering (returns thumbnail or full image URL)</summary>
    public override string GetPreviewData()
    {
        return !string.IsNullOrEmpty(ThumbnailUrl) ? ThumbnailUrl : ImageUrl;
    }
    
    // TODO: Add support for bounding box annotations when implementing annotation features
    // TODO: Add support for segmentation masks
    // TODO: Add support for keypoint annotations (pose detection, etc.)
    // TODO: Add support for image embeddings (for similarity search)
    // TODO: Add support for detected objects/labels from AI models
}
