using DatasetStudio.Core.Enumerations;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Settings.Components;

/// <summary>
/// Three-way theme choice (Light, Dark, System). Replaces a two-state icon toggle that could not
/// express <see cref="ThemeMode.Auto"/> and whose change callback the Settings page ignored.
/// </summary>
public partial class ThemeSelector
{
    /// <summary>The currently stored theme preference.</summary>
    [Parameter] public ThemeMode Value { get; set; } = ThemeMode.Dark;

    /// <summary>Raised with the newly chosen preference; the owner applies and persists it.</summary>
    [Parameter] public EventCallback<ThemeMode> ValueChanged { get; set; }

    private Task OnValueChangedAsync(ThemeMode value)
    {
        return ValueChanged.InvokeAsync(value);
    }
}
