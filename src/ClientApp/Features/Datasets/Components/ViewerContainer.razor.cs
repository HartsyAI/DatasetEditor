using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>Container component that dynamically renders the appropriate viewer based on dataset modality.</summary>
public partial class ViewerContainer : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ViewState ViewState { get; set; } = default!;

    /// <summary>Event callback when an item is selected.</summary>
    [Parameter] public EventCallback<DatasetItemDto> OnItemSelected { get; set; }

    /// <summary>Event callback when more items need to be loaded (for infinite scroll).</summary>
    [Parameter] public EventCallback OnLoadMore { get; set; }

    public Modality _modality = Modality.Image;
    public ViewMode _viewMode = ViewMode.Grid;
    private int _lastItemCount = -1;

    /// <summary>Fixed row height (px) used for the virtualized list view.</summary>
    private const double ListRowHeight = 168;

    /// <summary>Columns for the current view mode (single column in list view).</summary>
    private int CurrentColumns => _viewMode == ViewMode.List ? 1 : ViewState.GridColumns;

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        ViewState.OnChange += HandleViewStateChanged;
        DetermineModality();
        _viewMode = ViewState.ViewMode;
        _lastItemCount = DatasetState.Items.Count;
        Logs.Info("ViewerContainer initialized");
    }

    /// <summary>Raised when an item card/row is clicked.</summary>
    public Task HandleItemClick(DatasetItemDto item) => OnItemSelected.InvokeAsync(item);

    /// <summary>Toggles selection/favorite for an item and refreshes the view.</summary>
    public void HandleToggleSelection(DatasetItemDto item)
    {
        DatasetState.ToggleSelection(item);
        StateHasChanged();
    }

    /// <summary>Whether the given item is currently selected.</summary>
    public bool IsItemSelected(DatasetItemDto item) => DatasetState.IsSelected(item);

    // OnParametersSet removed - modality determined from DatasetState only

    /// <summary>Determines the modality of the current dataset.</summary>
    public void DetermineModality()
    {
        if (DatasetState.CurrentDataset != null)
        {
            _modality = DatasetState.CurrentDataset.Modality;
            Logs.Info($"Modality determined: {_modality}");
        }
        else if (DatasetState.Items.Count > 0)
        {
            // Infer modality from first item in DatasetState
            // DatasetItemDto doesn't have Modality property, default to Image
            _modality = Modality.Image;
            Logs.Info($"Modality inferred from items: {_modality}");
        }
        else
        {
            // Default to Image if no dataset or items
            _modality = Modality.Image;
            Logs.Info("Modality defaulted to Image");
        }
    }

    /// <summary>Handles dataset state changes and updates modality.</summary>
    public void HandleDatasetStateChanged()
    {
        Logs.Info($"[VIEWERCONTAINER] HandleDatasetStateChanged called, Items={DatasetState.Items.Count}");
        
        // Only determine modality if dataset changes, but don't re-render
        // When items are appended, Virtualize component handles rendering via ItemsProvider
        // We only need to re-render if the actual dataset or modality changes
        Modality previousModality = _modality;
        DetermineModality();

        // Re-render when the modality changes (new dataset) OR when the item count
        // changes (page appended / filter applied) so <Virtualize> sees the new window.
        // Virtualization keeps this cheap: only the visible rows actually re-render.
        int currentCount = DatasetState.Items.Count;
        if (_modality != previousModality || currentCount != _lastItemCount)
        {
            _lastItemCount = currentCount;
            StateHasChanged();
        }
    }

    /// <summary>Handles view state changes and updates view mode.</summary>
    public void HandleViewStateChanged()
    {
        _viewMode = ViewState.ViewMode;
        StateHasChanged();
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= HandleDatasetStateChanged;
        ViewState.OnChange -= HandleViewStateChanged;
    }
    
    // TODO: Add dynamic component loading for modality providers
    // TODO: Add caching of viewer components to avoid re-creation
    // TODO: Add transition animations when switching viewers
}
