using DatasetStudio.ClientApp.Shared.Theme;
using DatasetStudio.Core.Enumerations;
using MudBlazor;
using MudBlazor.Utilities;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.1 (audit U-13, U-21): the theme preference resolves to a palette, including Auto
/// following the OS, and the one shared theme carries the DESIGN.md §2.2 palette. Before this
/// PR Auto was unhandled (always dark) and the dark drawer used Tailwind gray instead of slate.
/// </summary>
public sealed class ThemeTests
{
    /// <summary>
    /// Light and Dark ignore the OS; System (Auto) follows it. Auto used to be unhandled, so a
    /// user who picked it always got dark.
    /// </summary>
    [Theory]
    [InlineData(ThemeMode.Light, false, false)]
    [InlineData(ThemeMode.Light, true, false)]
    [InlineData(ThemeMode.Dark, false, true)]
    [InlineData(ThemeMode.Dark, true, true)]
    [InlineData(ThemeMode.Auto, false, false)]
    [InlineData(ThemeMode.Auto, true, true)]
    public void IsDark_ResolvesEachModeAgainstTheSystemPreference(ThemeMode mode, bool systemPrefersDark, bool expected)
    {
        Assert.Equal(expected, ThemeModeResolver.IsDark(mode, systemPrefersDark));
    }

    /// <summary>
    /// A stored value with no implementation (HighContrast) must still render: it falls back to
    /// the product's dark default rather than throwing or picking light.
    /// </summary>
    [Fact]
    public void IsDark_UnimplementedModeFallsBackToTheDarkDefault()
    {
        Assert.True(ThemeModeResolver.IsDark(ThemeMode.HighContrast, systemPrefersDark: false));
    }

    /// <summary>
    /// The drawer's one-click toggle pins the opposite of what is showing, so toggling while on
    /// System never looks like a no-op.
    /// </summary>
    [Theory]
    [InlineData(true, ThemeMode.Light)]
    [InlineData(false, ThemeMode.Dark)]
    public void ToggleFrom_PinsTheOppositeOfWhatIsShowing(bool currentlyDark, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeModeResolver.ToggleFrom(currentlyDark));
    }

    /// <summary>
    /// Guards the DESIGN.md §2.2 palette: a slate-50 light page so white surfaces separate, an
    /// explicit placeholder grey, and slate (not Tailwind gray) for every dark neutral.
    /// </summary>
    [Fact]
    public void AppTheme_UsesTheDesignSystemPalette()
    {
        MudTheme theme = AppTheme.Theme;

        AssertColor("#F8FAFCFF", theme.PaletteLight.Background);
        AssertColor("#FFFFFFFF", theme.PaletteLight.Surface);
        AssertColor("#F1F5F9FF", theme.PaletteLight.BackgroundGray);

        AssertColor("#0F172AFF", theme.PaletteDark.Background);
        AssertColor("#1E293BFF", theme.PaletteDark.Surface);
        AssertColor("#0F172AFF", theme.PaletteDark.DrawerBackground);
        AssertColor("#334155FF", theme.PaletteDark.BackgroundGray);

        AssertColor("#2563EBFF", theme.PaletteLight.Primary);
        AssertColor("#3B82F6FF", theme.PaletteDark.Primary);
    }

    /// <summary>
    /// Every consumer must get the same instance; a second MudTheme is how palettes drifted before.
    /// </summary>
    [Fact]
    public void AppTheme_IsASingleSharedInstance()
    {
        Assert.Same(AppTheme.Theme, AppTheme.Theme);
    }

    private static void AssertColor(string expectedHexA, MudColor actual)
    {
        Assert.Equal(expectedHexA, actual.ToString(MudColorOutputFormats.HexA), ignoreCase: true);
    }
}
