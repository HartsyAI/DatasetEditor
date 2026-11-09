using Blazored.LocalStorage;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Enumerations;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Settings.Pages;

/// <summary>
/// Settings page. Every control reads from and writes to <see cref="ViewState"/> and saves to
/// LocalStorage immediately; previously each handler was a no-op and the page showed "TODO"
/// sentences to users (audit U-13).
/// </summary>
public partial class Settings : IDisposable
{
    [Inject] private ViewState ViewState { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    /// <summary>Re-renders when another surface (the drawer's theme button) changes the preferences.</summary>
    protected override void OnInitialized()
    {
        ViewState.OnChange += HandleViewStateChanged;
    }

    private Task OnThemeChangedAsync(ThemeMode mode)
    {
        ViewState.SetTheme(mode);
        return SaveAsync();
    }

    private Task OnViewModeChangedAsync(ViewMode mode)
    {
        ViewState.SetViewMode(mode);
        return SaveAsync();
    }

    private Task OnGridColumnsChangedAsync(int columns)
    {
        ViewState.SetGridColumns(columns);
        return SaveAsync();
    }

    private Task OnShowMetadataOverlayChangedAsync(bool show)
    {
        ViewState.SetShowMetadataOverlay(show);
        return SaveAsync();
    }

    private Task SaveAsync() => ViewState.SaveToStorageAsync(LocalStorage);

    private void HandleViewStateChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Unsubscribes from view-state changes.</summary>
    public void Dispose() => ViewState.OnChange -= HandleViewStateChanged;
}
