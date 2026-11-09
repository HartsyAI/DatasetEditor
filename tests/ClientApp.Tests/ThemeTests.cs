using DatasetStudio.ClientApp.Shared.Theme;
using DatasetStudio.Core.Enumerations;
using MudBlazor;
using Xunit;

namespace DatasetStudio.Tests.ClientApp
{
    /// <summary>
    /// PR 2.1 (audit U-13, U-21): the theme preference resolves to a palette, including Auto
    /// following the OS, and the one shared theme carries the DESIGN.md §2.2 palette. Before this
    /// PR Auto was unhandled (always dark) and the dark drawer used Tailwind gray instead of slate.
    /// </summary>
    public sealed class ThemeTests
    {
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

        [Fact]
        public void IsDark_UnimplementedModeFallsBackToTheDarkDefault()
        {
            Assert.True(ThemeModeResolver.IsDark(ThemeMode.HighContrast, systemPrefersDark: false));
        }

        [Theory]
        [InlineData(true, ThemeMode.Light)]
        [InlineData(false, ThemeMode.Dark)]
        public void ToggleFrom_PinsTheOppositeOfWhatIsShowing(bool currentlyDark, ThemeMode expected)
        {
            Assert.Equal(expected, ThemeModeResolver.ToggleFrom(currentlyDark));
        }

        [Fact]
        public void AppTheme_UsesTheDesignSystemPalette()
        {
            MudTheme theme = AppTheme.Theme;

            // Light: slate-50 page so white surfaces separate from it; slate-100 placeholders.
            Assert.Equal("#F8FAFCFF", theme.PaletteLight.Background.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#FFFFFFFF", theme.PaletteLight.Surface.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#F1F5F9FF", theme.PaletteLight.BackgroundGray.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);

            // Dark: every neutral is slate (the drawer used Tailwind gray #111827 before).
            Assert.Equal("#0F172AFF", theme.PaletteDark.Background.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#1E293BFF", theme.PaletteDark.Surface.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#0F172AFF", theme.PaletteDark.DrawerBackground.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#334155FF", theme.PaletteDark.BackgroundGray.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);

            Assert.Equal("#2563EBFF", theme.PaletteLight.Primary.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
            Assert.Equal("#3B82F6FF", theme.PaletteDark.Primary.ToString(MudBlazor.Utilities.MudColorOutputFormats.HexA), ignoreCase: true);
        }

        [Fact]
        public void AppTheme_IsASingleSharedInstance()
        {
            Assert.Same(AppTheme.Theme, AppTheme.Theme);
        }
    }
}
