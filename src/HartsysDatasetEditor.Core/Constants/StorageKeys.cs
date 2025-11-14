namespace HartsysDatasetEditor.Core.Constants;

/// <summary>Constants for browser LocalStorage keys</summary>
public static class StorageKeys
{
    // View settings
    public const string ViewSettings = "hartsy_view_settings";
    public const string Theme = "hartsy_theme";
    public const string Language = "hartsy_language";
    public const string ViewMode = "hartsy_view_mode";
    
    // Dataset state
    public const string CurrentDataset = "hartsy_current_dataset";
    public const string RecentDatasets = "hartsy_recent_datasets";
    public const string Favorites = "hartsy_favorites";
    
    // Filter state
    public const string LastFilters = "hartsy_last_filters";
    public const string SavedFilters = "hartsy_saved_filters";
    
    // User preferences
    public const string GridColumns = "hartsy_grid_columns";
    public const string ItemsPerPage = "hartsy_items_per_page";
    public const string ThumbnailSize = "hartsy_thumbnail_size";
    
    // TODO: Add more storage keys as features are added
}
