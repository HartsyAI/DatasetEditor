using Microsoft.AspNetCore.Components;
using MudBlazor;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Utilities;

namespace DatasetStudio.ClientApp.Shared.Layout;

/// <summary>Main application layout with app bar, drawer navigation, and theme management.</summary>
public partial class MainLayout : IDisposable
{
    [Inject] public NavigationService NavigationService { get; set; } = default!;
    [Inject] public ViewState ViewState { get; set; } = default!;

    public bool _drawerOpen = true;
    public bool _isDarkMode = false;
    public MudTheme _theme = new();

    /// <summary>Initializes component and subscribes to view state changes.</summary>
    protected override void OnInitialized()
    {
        ViewState.OnChange += StateHasChanged;
        _isDarkMode = ViewState.Theme == ThemeMode.Dark;
        ConfigureTheme();
        Logs.Info("MainLayout initialized");
    }

    /// <summary>Toggles the left navigation drawer open/closed.</summary>
    public void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
        Logs.Info($"Drawer toggled: {(_drawerOpen ? "open" : "closed")}");
    }

    /// <summary>Toggles between light and dark theme modes.</summary>
    public void ToggleTheme()
    {
        ThemeMode newTheme = _isDarkMode ? ThemeMode.Light : ThemeMode.Dark;
        _isDarkMode = !_isDarkMode;
        ViewState.SetTheme(newTheme);
        Logs.Info($"Theme toggled to: {newTheme}");
    }

    /// <summary>Navigates to the settings page.</summary>
    public void NavigateToSettings()
    {
        NavigationService.NavigateToSettings();
    }

    /// <summary>Configures the MudBlazor theme with custom colors and styles.</summary>
    public void ConfigureTheme()
    {
        _theme = new MudTheme()
        {
            PaletteLight = new PaletteLight()
            {
                Primary = "#2563EB",
                Secondary = "#64748B",
                Success = "#10B981",
                Error = "#EF4444",
                Warning = "#F59E0B",
                Info = "#06B6D4",
                AppbarBackground = "#FFFFFF",
                DrawerBackground = "#F9FAFB",
                Background = "#FFFFFF",
                Surface = "#FFFFFF"
            },
            PaletteDark = new PaletteDark()
            {
                Primary = "#3B82F6",
                Secondary = "#64748B",
                Success = "#10B981",
                Error = "#EF4444",
                Warning = "#F59E0B",
                Info = "#06B6D4",
                AppbarBackground = "#1F2937",
                DrawerBackground = "#111827",
                Background = "#0F172A",
                Surface = "#1E293B"
            },
            Typography = new Typography()
            {
                Default = new Default()
                {
                    FontFamily = ["Roboto", "Helvetica", "Arial", "sans-serif"]
                }
            }
        };
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose() => ViewState.OnChange -= StateHasChanged;

    // TODO: Add keyboard shortcut handling (Ctrl+B for drawer, Ctrl+T for theme)
    // TODO: Add responsive breakpoint handling for mobile
    // TODO: Add app bar overflow menu for additional actions
}
