using DatasetStudio.Core.Enumerations;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// The viewer's action bar (DESIGN.md §3.1), built on the shared <c>Toolbar</c>: filter toggle,
/// dataset name (the page's h1), item count, buffering and selection, view mode, details toggle
/// and the page cache switch. Extracted from <c>DatasetViewer</c>; all state is passed in and
/// every change goes back out through a callback.
/// </summary>
public partial class ViewerToolbar
{
    /// <summary>The open dataset's name.</summary>
    [Parameter, EditorRequired] public string DatasetName { get; set; } = string.Empty;

    /// <summary>"loaded / total items" or the filtered count.</summary>
    [Parameter] public string ItemCountLabel { get; set; } = string.Empty;

    /// <summary>Whether a page is being fetched.</summary>
    [Parameter] public bool IsBuffering { get; set; }

    /// <summary>How many items are selected.</summary>
    [Parameter] public int SelectedCount { get; set; }

    /// <summary>Current view mode.</summary>
    [Parameter] public ViewMode ViewMode { get; set; } = ViewMode.Grid;

    /// <summary>Raised with the chosen view mode.</summary>
    [Parameter] public EventCallback<ViewMode> OnViewModeChanged { get; set; }

    /// <summary>Whether the filter panel is open.</summary>
    [Parameter] public bool FiltersOpen { get; set; }

    /// <summary>Opens or closes the filter panel.</summary>
    [Parameter] public EventCallback OnToggleFilters { get; set; }

    /// <summary>Whether the detail panel is open.</summary>
    [Parameter] public bool DetailsOpen { get; set; }

    /// <summary>Opens or closes the detail panel.</summary>
    [Parameter] public EventCallback OnToggleDetails { get; set; }

    /// <summary>Whether the IndexedDB page cache is on.</summary>
    [Parameter] public bool IndexedDbEnabled { get; set; }

    /// <summary>Raised when the page cache switch changes.</summary>
    [Parameter] public EventCallback<bool> OnIndexedDbChanged { get; set; }

    /// <summary>Whether the user may manage shares; shows the Share button.</summary>
    [Parameter] public bool CanShare { get; set; }

    /// <summary>Opens the share dialog.</summary>
    [Parameter] public EventCallback OnShare { get; set; }
}
