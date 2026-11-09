namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Column and row geometry for the virtualized image grid. The column count comes from the
/// container width (DESIGN.md §8: min card width 180 px), capped by the user's "Grid columns"
/// preference. It used to be the preference alone, so a narrow window got 4 columns of 60 px cards.
/// </summary>
public static class GridLayout
{
    /// <summary>Smallest card width before a column is dropped.</summary>
    public const double MinCardWidth = 180;

    /// <summary>Gap between cards, matching <c>--ds-grid-gap</c> at desktop widths.</summary>
    public const double Gap = 16;

    /// <summary>Horizontal padding on each side of a grid row.</summary>
    public const double SidePadding = 16;

    /// <summary>Returns how many columns fit, at least 1 and at most <paramref name="preferredColumns"/>.</summary>
    /// <param name="containerWidth">The scroll container's width in px; 0 or less when unknown.</param>
    /// <param name="preferredColumns">The user's preferred column count.</param>
    public static int ColumnsFor(double containerWidth, int preferredColumns)
    {
        int preferred = Math.Max(1, preferredColumns);
        if (containerWidth <= 0)
        {
            // Width not measured yet: render the preference and correct after the first measure.
            return preferred;
        }

        double usable = containerWidth - (2 * SidePadding);
        int fit = (int)Math.Floor((usable + Gap) / (MinCardWidth + Gap));
        return Math.Clamp(fit, 1, preferred);
    }

    /// <summary>Row height for square cards at a given width and column count (card plus gap).</summary>
    /// <param name="containerWidth">The scroll container's width in px.</param>
    /// <param name="columns">The column count in use.</param>
    public static double SquareRowHeight(double containerWidth, int columns)
    {
        int cols = Math.Max(1, columns);
        double cardWidth = (containerWidth - (2 * SidePadding) - ((cols - 1) * Gap)) / cols;
        return Math.Max(40, cardWidth) + Gap;
    }
}
