using DatasetStudio.Core.Enumerations;

namespace DatasetStudio.ClientApp.Shared.Theme;

/// <summary>
/// Turns the stored <see cref="ThemeMode"/> preference into the dark/light flag that
/// <c>MudThemeProvider</c> takes. Kept separate from the layout so the rule (including
/// <see cref="ThemeMode.Auto"/> following the OS) is testable without rendering.
/// </summary>
public static class ThemeModeResolver
{
    /// <summary>Returns whether the dark palette should be shown.</summary>
    /// <param name="mode">The user's stored preference.</param>
    /// <param name="systemPrefersDark">The browser's <c>prefers-color-scheme: dark</c> result.</param>
    public static bool IsDark(ThemeMode mode, bool systemPrefersDark)
    {
        return mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            ThemeMode.Auto => systemPrefersDark,
            // Dark is the product default (DESIGN.md §1.5), so an unknown stored value falls back to it.
            _ => true
        };
    }

    /// <summary>
    /// The explicit mode a one-click toggle should switch to from what is currently shown.
    /// Toggling from Auto pins the opposite of the current appearance rather than cycling
    /// through Auto again, which would look like a no-op when the OS already matches.
    /// </summary>
    /// <param name="currentlyDark">Whether the dark palette is showing now.</param>
    public static ThemeMode ToggleFrom(bool currentlyDark)
    {
        return currentlyDark ? ThemeMode.Light : ThemeMode.Dark;
    }
}
