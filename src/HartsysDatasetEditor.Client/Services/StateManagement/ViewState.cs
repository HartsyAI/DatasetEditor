using HartsysDatasetEditor.Core.Constants;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Utilities;
using Blazored.LocalStorage;

namespace HartsysDatasetEditor.Client.Services.StateManagement;

/// <summary>Manages UI view preferences and display settings with LocalStorage persistence.</summary>
public class ViewState
{
    /// <summary>Current view settings containing all user preferences.</summary>
    public ViewSettings Settings { get; private set; } = new();
    
    /// <summary>Controls visibility of the left filter panel.</summary>
    public bool ShowFilterPanel { get; set; } = true;
    
    /// <summary>Controls visibility of the right detail panel.</summary>
    public bool ShowDetailPanel { get; set; } = true;
    
    /// <summary>Current view mode (Grid, List, or Gallery).</summary>
    public ViewMode ViewMode => Settings.ViewMode;
    
    /// <summary>Current theme mode (Light, Dark, or Auto).</summary>
    public ThemeMode Theme => Settings.Theme;
    
    /// <summary>Number of columns in grid view.</summary>
    public int GridColumns => Settings.GridColumns;
    
    /// <summary>Number of items to display per page.</summary>
    public int ItemsPerPage => Settings.ItemsPerPage;
    
    /// <summary>Event fired when view settings change.</summary>
    public event Action? OnChange;
    
    /// <summary>Updates all view settings at once, replacing existing settings.</summary>
    /// <param name="settings">New view settings to apply.</param>
    public void UpdateSettings(ViewSettings settings)
    {
        Settings = settings;
        NotifyStateChanged();
        Logs.Info("View settings updated");
    }
    
    /// <summary>Changes the current view mode (Grid, List, Gallery).</summary>
    /// <param name="mode">View mode to switch to.</param>
    public void SetViewMode(ViewMode mode)
    {
        Settings.ViewMode = mode;
        NotifyStateChanged();
        Logs.Info($"View mode changed to: {mode}");
    }
    
    /// <summary>Changes the application theme.</summary>
    /// <param name="theme">Theme mode to apply (Light, Dark, Auto).</param>
    public void SetTheme(ThemeMode theme)
    {
        Settings.Theme = theme;
        NotifyStateChanged();
        Logs.Info($"Theme changed to: {theme}");
    }
    
    /// <summary>Sets the number of columns for grid view.</summary>
    /// <param name="columns">Number of columns (1-8).</param>
    public void SetGridColumns(int columns)
    {
        if (columns < 1 || columns > 8)
        {
            Logs.Error($"Invalid grid column count: {columns}. Must be between 1 and 8.");
            return;
        }
        
        Settings.GridColumns = columns;
        NotifyStateChanged();
        Logs.Info($"Grid columns set to: {columns}");
    }
    
    /// <summary>Sets the number of items to display per page.</summary>
    /// <param name="itemsPerPage">Items per page (10-200).</param>
    public void SetItemsPerPage(int itemsPerPage)
    {
        if (itemsPerPage < 10 || itemsPerPage > 200)
        {
            Logs.Error($"Invalid items per page: {itemsPerPage}. Must be between 10 and 200.");
            return;
        }
        
        Settings.ItemsPerPage = itemsPerPage;
        NotifyStateChanged();
        Logs.Info($"Items per page set to: {itemsPerPage}");
    }
    
    /// <summary>Changes the application language.</summary>
    /// <param name="language">Language code (e.g., "en", "es").</param>
    public void SetLanguage(string language)
    {
        Settings.Language = language;
        NotifyStateChanged();
        Logs.Info($"Language changed to: {language}");
    }
    
    /// <summary>Toggles the visibility of the filter panel.</summary>
    public void ToggleFilterPanel()
    {
        ShowFilterPanel = !ShowFilterPanel;
        NotifyStateChanged();
        Logs.Info($"Filter panel visibility: {ShowFilterPanel}");
    }
    
    /// <summary>Toggles the visibility of the detail panel.</summary>
    public void ToggleDetailPanel()
    {
        ShowDetailPanel = !ShowDetailPanel;
        NotifyStateChanged();
        Logs.Info($"Detail panel visibility: {ShowDetailPanel}");
    }
    
    /// <summary>Sets whether to show image metadata overlays on hover.</summary>
    /// <param name="show">True to show overlays, false to hide.</param>
    public void SetShowMetadataOverlay(bool show)
    {
        Settings.ShowMetadataOverlay = show;
        NotifyStateChanged();
    }
    
    /// <summary>Sets whether to enable lazy loading for images.</summary>
    /// <param name="enable">True to enable lazy loading, false to disable.</param>
    public void SetLazyLoading(bool enable)
    {
        Settings.EnableLazyLoading = enable;
        NotifyStateChanged();
    }
    
    /// <summary>Loads view settings from browser LocalStorage.</summary>
    /// <param name="storage">LocalStorage service instance.</param>
    public async Task LoadFromStorageAsync(ILocalStorageService storage)
    {
        try
        {
            ViewSettings? savedSettings = await storage.GetItemAsync<ViewSettings>(StorageKeys.ViewSettings);
            if (savedSettings != null)
            {
                Settings = savedSettings;
                NotifyStateChanged();
                Logs.Info("View settings loaded from LocalStorage");
            }
            else
            {
                Logs.Info("No saved view settings found, using defaults");
            }
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to load view settings from LocalStorage", ex);
        }
    }
    
    /// <summary>Saves current view settings to browser LocalStorage.</summary>
    /// <param name="storage">LocalStorage service instance.</param>
    public async Task SaveToStorageAsync(ILocalStorageService storage)
    {
        try
        {
            await storage.SetItemAsync(StorageKeys.ViewSettings, Settings);
            Logs.Info("View settings saved to LocalStorage");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to save view settings to LocalStorage", ex);
        }
    }
    
    /// <summary>Resets all view settings to their default values.</summary>
    public void ResetToDefaults()
    {
        Settings = new ViewSettings();
        ShowFilterPanel = true;
        ShowDetailPanel = true;
        NotifyStateChanged();
        Logs.Info("View settings reset to defaults");
    }
    
    /// <summary>Notifies all subscribers that the view state has changed.</summary>
    protected void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
    
    // TODO: Add keyboard shortcut preferences
    // TODO: Add thumbnail size preferences
    // TODO: Add sorting preferences (date, name, size, etc.)
    // TODO: Add view state presets for quick switching
}
