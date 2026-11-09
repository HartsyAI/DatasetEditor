using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Theme;

/// <summary>
/// The one <see cref="MudTheme"/> for the client (DESIGN.md §2.2). Palette values live here and
/// nowhere else: components reference them through MudBlazor's <c>Color</c> parameters or the
/// <c>--mud-palette-*</c> custom properties, and app-level tokens that MudBlazor does not own are
/// <c>--ds-*</c> properties in <c>wwwroot/css/app.css</c>. Before this, the theme was built inline
/// in <c>MainLayout</c>, and the dark drawer used Tailwind gray while the rest of the app used slate.
/// </summary>
public static class AppTheme
{
    /// <summary>The shared theme instance passed to <c>MudThemeProvider</c>.</summary>
    public static MudTheme Theme { get; } = Create();

    /// <summary>
    /// Builds the theme. The light background is slate-50 rather than white so surfaces (white)
    /// separate from the page; <c>BackgroundGray</c> is set explicitly because Mud's neutral
    /// default clashes with the slate surfaces in image placeholders and skeletons.
    /// </summary>
    private static MudTheme Create()
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#2563EB",
                Secondary = "#64748B",
                Success = "#10B981",
                Warning = "#F59E0B",
                Error = "#EF4444",
                Info = "#06B6D4",
                Background = "#F8FAFC",
                Surface = "#FFFFFF",
                BackgroundGray = "#F1F5F9",
                DrawerBackground = "#F9FAFB",
                AppbarBackground = "#FFFFFF"
            },
            PaletteDark = new PaletteDark
            {
                Primary = "#3B82F6",
                Secondary = "#64748B",
                Success = "#10B981",
                Warning = "#F59E0B",
                Error = "#EF4444",
                Info = "#06B6D4",
                Background = "#0F172A",
                Surface = "#1E293B",
                BackgroundGray = "#334155",
                DrawerBackground = "#0F172A",
                AppbarBackground = "#1E293B"
            },
            Typography = new Typography
            {
                Default = new Default
                {
                    FontFamily = ["Roboto", "Helvetica", "Arial", "sans-serif"]
                }
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "4px"
            }
        };
    }
}
