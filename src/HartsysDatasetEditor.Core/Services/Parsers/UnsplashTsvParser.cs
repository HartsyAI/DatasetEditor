using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Core.Services.Parsers;

/// <summary>Parser for Unsplash dataset TSV format (photos.tsv file structure)</summary>
public class UnsplashTsvParser : BaseTsvParser
{
    /// <summary>Gets the modality type (Image for Unsplash datasets)</summary>
    public override Modality ModalityType => Modality.Image;
    
    /// <summary>Gets the parser name</summary>
    public override string Name => "Unsplash TSV Parser";
    
    /// <summary>Gets the parser description</summary>
    public override string Description => "Parses Unsplash dataset TSV files containing photo metadata and URLs";
    
    /// <summary>Checks if this parser can handle Unsplash-specific TSV format</summary>
    public override bool CanParse(string fileContent, string fileName)
    {
        // First check basic TSV structure
        if (!base.CanParse(fileContent, fileName))
        {
            return false;
        }
        
        // Check for Unsplash-specific column names in header
        string firstLine = fileContent.Split('\n')[0];
        
        // Unsplash TSV files have specific columns like photo_id, photo_image_url, photographer_username
        bool hasUnsplashColumns = firstLine.Contains("photo_id") &&
                                  firstLine.Contains("photo_image_url") &&
                                  firstLine.Contains("photographer_username");
        
        return hasUnsplashColumns;
    }
    
    /// <summary>Parses Unsplash TSV content and yields ImageItem objects</summary>
    public override async IAsyncEnumerable<IDatasetItem> ParseAsync(
        string fileContent, 
        string datasetId, 
        Dictionary<string, string>? options = null)
    {
        Logs.Info($"Starting Unsplash TSV parse for dataset {datasetId}");
        
        string[] lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length < 2)
        {
            Logs.Warning("TSV file has no data rows");
            yield break;
        }
        
        // Parse header row
        string[] headers = ParseHeader(lines[0]);
        Logs.Info($"Parsed {headers.Length} columns from header");
        
        // Parse each data row
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = ParseRow(lines[i]);
            
            // Skip rows with mismatched column count
            if (values.Length != headers.Length)
            {
                Logs.Warning($"Skipping row {i + 1}: column count mismatch");
                continue;
            }
            
            // Create ImageItem from row data
            ImageItem item = CreateImageItemFromRow(headers, values, datasetId);
            
            // Allow async operation (for future streaming scenarios)
            await Task.Yield();
            
            yield return item;
        }
        
        Logs.Info($"Completed parsing {lines.Length - 1} items");
    }
    
    /// <summary>Creates an ImageItem from parsed TSV row data</summary>
    private ImageItem CreateImageItemFromRow(string[] headers, string[] values, string datasetId)
    {
        // Unsplash TSV column mapping based on documentation
        // Reference: https://github.com/unsplash/datasets/blob/master/DOCS.md
        
        ImageItem item = new ImageItem
        {
            Id = GetColumnValue(headers, values, "photo_id"),
            DatasetId = datasetId,
            ImageUrl = GetColumnValue(headers, values, "photo_image_url"),
            SourcePath = GetColumnValue(headers, values, "photo_url"), // Unsplash page URL
            Title = GetColumnValue(headers, values, "photo_description", "Untitled"),
            Description = GetColumnValue(headers, values, "photo_description"),
            Width = GetIntValue(headers, values, "photo_width"),
            Height = GetIntValue(headers, values, "photo_height"),
            Photographer = GetColumnValue(headers, values, "photographer_first_name") + " " + 
                          GetColumnValue(headers, values, "photographer_last_name"),
            PhotographerUsername = GetColumnValue(headers, values, "photographer_username"),
            PhotographerUrl = GetColumnValue(headers, values, "photographer_url"),
            Views = GetIntValue(headers, values, "photo_views"),
            Downloads = GetIntValue(headers, values, "photo_downloads"),
            Likes = GetIntValue(headers, values, "photo_likes"),
            Location = GetColumnValue(headers, values, "photo_location_name"),
            AverageColor = GetColumnValue(headers, values, "avg_color"),
            CreatedAt = GetDateTimeValue(headers, values, "photo_submitted_at") ?? DateTime.UtcNow,
            UpdatedAt = GetDateTimeValue(headers, values, "photo_updated_at") ?? DateTime.UtcNow
        };
        
        // Parse AI-generated description if available
        string aiDescription = GetColumnValue(headers, values, "ai_description");
        if (!string.IsNullOrWhiteSpace(aiDescription))
        {
            item.Metadata["ai_description"] = aiDescription;
        }
        
        // Parse AI-generated tags/keywords if available (from keywords.tsv in full dataset)
        // TODO: Handle keywords when parsing keywords.tsv file
        
        // Parse location coordinates if available
        string latitude = GetColumnValue(headers, values, "photo_location_latitude");
        string longitude = GetColumnValue(headers, values, "photo_location_longitude");
        
        if (!string.IsNullOrEmpty(latitude) && !string.IsNullOrEmpty(longitude))
        {
            if (double.TryParse(latitude, out double lat) && double.TryParse(longitude, out double lon))
            {
                item.Latitude = lat;
                item.Longitude = lon;
            }
        }
        
        // Add any EXIF data columns to metadata
        AddExifMetadata(item, headers, values);
        
        // Generate thumbnail URL from Unsplash's dynamic image URL
        // Unsplash supports URL parameters for resizing: ?w=400&q=80
        item.ThumbnailUrl = !string.IsNullOrEmpty(item.ImageUrl) 
            ? $"{item.ImageUrl}?w=400&q=80" 
            : item.ImageUrl;
        
        // Estimate file size if not provided (rough estimate based on dimensions)
        if (item.FileSizeBytes == 0 && item.Width > 0 && item.Height > 0)
        {
            // Rough estimate: ~3 bytes per pixel for JPEG
            item.FileSizeBytes = (long)(item.Width * item.Height * 3 * 0.3); // 30% compression ratio
        }
        
        return item;
    }
    
    /// <summary>Adds EXIF metadata from TSV columns to the item</summary>
    private void AddExifMetadata(ImageItem item, string[] headers, string[] values)
    {
        // Common EXIF fields that might be in Unsplash dataset
        string[] exifFields = new[]
        {
            "exif_camera_make",
            "exif_camera_model",
            "exif_iso",
            "exif_aperture_value",
            "exif_focal_length",
            "exif_exposure_time"
        };
        
        foreach (string field in exifFields)
        {
            string value = GetColumnValue(headers, values, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Store in ExifData dictionary with cleaned key name
                string key = field.Replace("exif_", "").Replace("_", " ");
                item.ExifData[key] = value;
            }
        }
    }
    
    /// <summary>Validates Unsplash TSV structure including required columns</summary>
    public override (bool IsValid, List<string> Errors) Validate(string fileContent)
    {
        // First run base validation
        (bool isValid, List<string> errors) = base.Validate(fileContent);
        
        if (!isValid)
        {
            return (false, errors);
        }
        
        // Check for required Unsplash columns
        string[] lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] headers = ParseHeader(lines[0]);
        
        string[] requiredColumns = new[] { "photo_id", "photo_image_url" };
        
        foreach (string required in requiredColumns)
        {
            if (!headers.Contains(required))
            {
                errors.Add($"Missing required column: {required}");
            }
        }
        
        return (errors.Count == 0, errors);
    }
    
    // TODO: Add support for parsing keywords.tsv file (separate file with photo-keyword pairs)
    // TODO: Add support for parsing collections.tsv file (photo-collection relationships)
    // TODO: Add support for parsing conversions.tsv file (download/search data)
    // TODO: Add support for parsing colors.tsv file (dominant colors data)
    // TODO: Add support for merging multiple TSV files using photo_id as key
}
