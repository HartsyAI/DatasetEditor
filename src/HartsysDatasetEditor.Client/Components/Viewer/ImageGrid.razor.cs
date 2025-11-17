using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Viewer;

/// <summary>Virtualized grid component for displaying image items with billion-scale performance.</summary>
/// <remarks>Uses INTERNAL _items field that directly references DatasetState.Items to prevent parameter change detection.</remarks>
public partial class ImageGrid : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ViewState ViewState { get; set; } = default!;

    /// <summary>Event callback when an item is selected for detail view.</summary>
    [Parameter] public EventCallback<IDatasetItem> OnItemSelected { get; set; }

    public int _gridColumns = 4;
    private List<IDatasetItem> _items = new();

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        ViewState.OnChange += HandleViewStateChanged;
        DatasetState.OnChange += HandleDatasetStateChanged;
        _gridColumns = ViewState.GridColumns;
        _items = DatasetState.Items; // Direct reference - when items append, list grows automatically
        Logs.Info($"[ImageGrid] Initialized with {_gridColumns} columns, {_items.Count} items");
    }

    private void HandleDatasetStateChanged()
    {
        // CRITICAL: Just update the reference if it changed (filters applied)
        // DO NOT call StateHasChanged - Virtualize will detect the list grew automatically
        if (_items != DatasetState.Items)
        {
            Logs.Info($"[ImageGrid] Items reference changed (filters applied), updating");
            _items = DatasetState.Items;
            // Force re-render ONLY when reference changes (filters)
            StateHasChanged();
        }
        else
        {
            Logs.Info($"[ImageGrid] Items appended ({DatasetState.Items.Count} total), Virtualize will handle it");
            // DO NOT call StateHasChanged - items just appended to same list
        }
    }

    /// <summary>Handles view state changes to update grid column count.</summary>
    public void HandleViewStateChanged()
    {
        _gridColumns = ViewState.GridColumns;
        StateHasChanged();
    }

    /// <summary>Handles click event on an image card.</summary>
    /// <param name="item">Clicked dataset item.</param>
    public async Task HandleItemClick(IDatasetItem item)
    {
        await OnItemSelected.InvokeAsync(item);
        Logs.Info($"Image clicked: {item.Id}");
    }

    /// <summary>Handles selection toggle for an item (checkbox click).</summary>
    /// <param name="item">Item to toggle selection for.</param>
    public void HandleToggleSelection(IDatasetItem item)
    {
        DatasetState.ToggleSelection(item);
        StateHasChanged();
    }

    /// <summary>Checks if a specific item is currently selected.</summary>
    /// <param name="item">Item to check selection status.</param>
    /// <returns>True if item is selected, false otherwise.</returns>
    public bool IsItemSelected(IDatasetItem item)
    {
        return DatasetState.IsSelected(item);
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        ViewState.OnChange -= HandleViewStateChanged;
        DatasetState.OnChange -= HandleDatasetStateChanged;
    }
}
