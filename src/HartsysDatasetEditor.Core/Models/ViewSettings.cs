using HartsysDatasetEditor.Core.Enums;

namespace HartsysDatasetEditor.Core.Models;

/// <summary>Represents user preferences for viewing datasets</summary>
public class ViewSettings
{
    /// <summary>Preferred view mode (Grid, List, Gallery, etc.)</summary>
    public ViewMode ViewMode { get; set; } = ViewMode.Grid;
    
    /// <summary>Theme mode preference (Light, Dark, Auto)</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    
    /// <summary>Preferred language code (en, es, fr, de, etc.)</summary>
    public string Language { get; set; } = "en";
    
    /// <summary>Number of items to display per page</summary>
    public int ItemsPerPage { get; set; } = 50;
    
    /// <summary>Grid column count (for grid view mode)</summary>
    public int GridColumns { get; set; } = 4;
    
    /// <summary>Thumbnail size preference (small, medium, large)</summary>
    public string ThumbnailSize { get; set; } = "medium";
    
    /// <summary>Whether to show metadata overlays on hover</summary>
    public bool ShowMetadataOverlay { get; set; } = true;
    
    /// <summary>Whether to show image dimensions in cards</summary>
    public bool ShowDimensions { get; set; } = true;
    
    /// <summary>Whether to show file size in cards</summary>
    public bool ShowFileSize { get; set; } = true;
    
    /// <summary>Whether to show photographer info in cards</summary>
    public bool ShowPhotographer { get; set; } = true;
    
    /// <summary>Whether to enable image lazy loading</summary>
    public bool EnableLazyLoading { get; set; } = true;
    
    /// <summary>Whether to auto-play videos in gallery mode</summary>
    public bool AutoPlayVideos { get; set; } = false;
    
    /// <summary>Slideshow interval in seconds (for gallery mode)</summary>
    public int SlideshowIntervalSeconds { get; set; } = 3;
    
    /// <summary>Default sort field (createdAt, title, size, etc.)</summary>
    public string SortField { get; set; } = "createdAt";
    
    /// <summary>Default sort direction (ascending or descending)</summary>
    public bool SortDescending { get; set; } = true;
    
    /// <summary>Whether to remember last used filters per dataset</summary>
    public bool RememberFilters { get; set; } = true;
    
    /// <summary>Whether to show filter panel by default</summary>
    public bool ShowFilterPanel { get; set; } = true;
    
    /// <summary>Whether to show detail panel by default</summary>
    public bool ShowDetailPanel { get; set; } = true;
    
    /// <summary>Custom CSS class for additional theming - TODO: Implement custom theme system</summary>
    public string CustomThemeClass { get; set; } = string.Empty;
    
    /// <summary>Accessibility: High contrast mode</summary>
    public bool HighContrastMode { get; set; } = false;
    
    /// <summary>Accessibility: Reduce motion/animations</summary>
    public bool ReduceMotion { get; set; } = false;
    
    /// <summary>Accessibility: Screen reader optimizations</summary>
    public bool ScreenReaderMode { get; set; } = false;
    
    // TODO: Add support for custom column visibility in list view
    // TODO: Add support for keyboard shortcut customization
    // TODO: Add support for layout presets (save/load custom layouts)
    // TODO: Add support for per-modality settings (different settings for images vs video)
}
