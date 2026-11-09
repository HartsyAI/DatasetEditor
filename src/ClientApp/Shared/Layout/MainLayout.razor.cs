using Blazored.LocalStorage;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Theme;
using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Layout;

/// <summary>
/// Application shell: navigation drawer, the theme provider, and the theme preference. The stored
/// <see cref="ThemeMode"/> is loaded from LocalStorage on start, applied (Auto follows the OS and
/// keeps following it while the app is open), and saved whenever it changes. Before this the
/// toggle had no caller and the preference was never loaded or saved, so the app was always dark.
/// </summary>
public partial class MainLayout : IDisposable
{
    [Inject] private ViewState ViewState { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    private MudThemeProvider? _themeProvider;
    private bool _drawerOpen = true;
    private bool _isDarkMode = true;
    // Dark until the browser reports otherwise: matches the product default and the boot screen.
    private bool _systemPrefersDark = true;

    private string ThemeToggleLabel => _isDarkMode ? "Use light theme" : "Use dark theme";

    /// <summary>Loads the saved preferences before the first render so the theme doesn't flash.</summary>
    protected override async Task OnInitializedAsync()
    {
        ViewState.OnChange += HandleViewStateChanged;
        await ViewState.LoadFromStorageAsync(LocalStorage);
        ApplyThemeMode();
    }

    /// <summary>
    /// Reads and then watches the OS colour scheme. This needs the provider's JS module, which only
    /// exists after the first render, so Auto briefly shows the default until this completes.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _themeProvider is null)
        {
            return;
        }

        try
        {
            _systemPrefersDark = await _themeProvider.GetSystemPreference();
            await _themeProvider.WatchSystemPreference(OnSystemPreferenceChangedAsync);
        }
        catch (Exception ex)
        {
            // A browser without matchMedia support keeps the dark default; not worth failing the shell.
            Logs.Warning($"Could not read the system colour scheme: {ex.Message}");
        }

        if (ApplyThemeMode())
        {
            StateHasChanged();
        }
    }

    private void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
    }

    /// <summary>Flips between light and dark, pinning an explicit mode, and saves it.</summary>
    private async Task ToggleThemeAsync()
    {
        ViewState.SetTheme(ThemeModeResolver.ToggleFrom(_isDarkMode));
        await ViewState.SaveToStorageAsync(LocalStorage);
    }

    private Task OnSystemPreferenceChangedAsync(bool prefersDark)
    {
        _systemPrefersDark = prefersDark;
        if (ApplyThemeMode())
        {
            return InvokeAsync(StateHasChanged);
        }
        return Task.CompletedTask;
    }

    private void HandleViewStateChanged()
    {
        ApplyThemeMode();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Recomputes the dark flag; returns whether it changed.</summary>
    private bool ApplyThemeMode()
    {
        bool isDark = ThemeModeResolver.IsDark(ViewState.Theme, _systemPrefersDark);
        if (isDark == _isDarkMode)
        {
            return false;
        }
        _isDarkMode = isDark;
        return true;
    }

    /// <summary>Unsubscribes from view-state changes.</summary>
    public void Dispose() => ViewState.OnChange -= HandleViewStateChanged;
}
