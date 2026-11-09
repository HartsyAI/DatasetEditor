using DatasetStudio.Core.Enumerations;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Settings.Components;

/// <summary>
/// Default view mode, grid column count and hover details for the viewer. The owner applies and
/// persists each change. The slider now spans 1–8, the range <c>ViewState.SetGridColumns</c>
/// accepts (it stopped at 6), and the toggle is typed on <see cref="ViewMode"/> instead of
/// round-tripping through strings.
/// </summary>
public partial class ViewPreferences
{
    /// <summary>Selected default view mode.</summary>
    [Parameter] public ViewMode SelectedViewMode { get; set; } = ViewMode.Grid;

    /// <summary>Raised with the newly chosen view mode.</summary>
    [Parameter] public EventCallback<ViewMode> OnViewModeChanged { get; set; }

    /// <summary>Grid column count.</summary>
    [Parameter] public int GridColumns { get; set; } = 4;

    /// <summary>Raised as the column slider moves.</summary>
    [Parameter] public EventCallback<int> OnGridColumnsChanged { get; set; }

    /// <summary>Whether image cards show details on hover.</summary>
    [Parameter] public bool ShowMetadataOverlay { get; set; } = true;

    /// <summary>Raised when the hover-details switch changes.</summary>
    [Parameter] public EventCallback<bool> OnShowMetadataOverlayChanged { get; set; }
}
