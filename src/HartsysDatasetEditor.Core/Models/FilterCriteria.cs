namespace HartsysDatasetEditor.Core.Models;

/// <summary>Represents filter criteria for querying dataset items</summary>
public class FilterCriteria
{
    /// <summary>Text search query (searches across title, description, tags, etc.)</summary>
    public string SearchQuery { get; set; } = string.Empty;
    
    /// <summary>Filter by specific tags (AND logic - item must have all tags)</summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>Filter by date range - start date</summary>
    public DateTime? DateFrom { get; set; }
    
    /// <summary>Filter by date range - end date</summary>
    public DateTime? DateTo { get; set; }
    
    /// <summary>Filter by favorites only</summary>
    public bool? FavoritesOnly { get; set; }
    
    /// <summary>Minimum file size in bytes (for image datasets)</summary>
    public long? MinFileSizeBytes { get; set; }
    
    /// <summary>Maximum file size in bytes (for image datasets)</summary>
    public long? MaxFileSizeBytes { get; set; }
    
    /// <summary>Minimum width in pixels (for image datasets)</summary>
    public int? MinWidth { get; set; }
    
    /// <summary>Maximum width in pixels (for image datasets)</summary>
    public int? MaxWidth { get; set; }
    
    /// <summary>Minimum height in pixels (for image datasets)</summary>
    public int? MinHeight { get; set; }
    
    /// <summary>Maximum height in pixels (for image datasets)</summary>
    public int? MaxHeight { get; set; }
    
    /// <summary>Filter by aspect ratio range - minimum</summary>
    public double? MinAspectRatio { get; set; }
    
    /// <summary>Filter by aspect ratio range - maximum</summary>
    public double? MaxAspectRatio { get; set; }
    
    /// <summary>Filter by specific image formats (JPEG, PNG, WebP, etc.)</summary>
    public List<string> Formats { get; set; } = new();
    
    /// <summary>Filter by photographer/creator name</summary>
    public string Photographer { get; set; } = string.Empty;
    
    /// <summary>Filter by location/place name</summary>
    public string Location { get; set; } = string.Empty;
    
    /// <summary>Custom metadata filters as key-value pairs</summary>
    public Dictionary<string, string> CustomFilters { get; set; } = new();
    
    /// <summary>Checks if any filters are active</summary>
    public bool HasActiveFilters()
    {
        return !string.IsNullOrWhiteSpace(SearchQuery) ||
               Tags.Any() ||
               DateFrom.HasValue ||
               DateTo.HasValue ||
               FavoritesOnly.HasValue ||
               MinFileSizeBytes.HasValue ||
               MaxFileSizeBytes.HasValue ||
               MinWidth.HasValue ||
               MaxWidth.HasValue ||
               MinHeight.HasValue ||
               MaxHeight.HasValue ||
               MinAspectRatio.HasValue ||
               MaxAspectRatio.HasValue ||
               Formats.Any() ||
               !string.IsNullOrWhiteSpace(Photographer) ||
               !string.IsNullOrWhiteSpace(Location) ||
               CustomFilters.Any();
    }
    
    /// <summary>Resets all filters to default empty state</summary>
    public void Clear()
    {
        SearchQuery = string.Empty;
        Tags.Clear();
        DateFrom = null;
        DateTo = null;
        FavoritesOnly = null;
        MinFileSizeBytes = null;
        MaxFileSizeBytes = null;
        MinWidth = null;
        MaxWidth = null;
        MinHeight = null;
        MaxHeight = null;
        MinAspectRatio = null;
        MaxAspectRatio = null;
        Formats.Clear();
        Photographer = string.Empty;
        Location = string.Empty;
        CustomFilters.Clear();
    }
    
    // TODO: Add support for complex query builder (AND/OR logic between criteria)
    // TODO: Add support for saved filter presets
    // TODO: Add support for filter templates per dataset type
}
