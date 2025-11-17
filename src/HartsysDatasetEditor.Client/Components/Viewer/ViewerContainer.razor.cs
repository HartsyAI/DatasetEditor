using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Viewer;

/// <summary>Container component that dynamically renders the appropriate viewer based on dataset modality.</summary>
public partial class ViewerContainer : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ViewState ViewState { get; set; } = default!;

    /// <summary>Event callback when an item is selected.</summary>
    [Parameter] public EventCallback<IDatasetItem> OnItemSelected { get; set; }

    /// <summary>Event callback when more items need to be loaded (for infinite scroll).</summary>
    [Parameter] public EventCallback OnLoadMore { get; set; }

    public Modality _modality = Modality.Image;
    public ViewMode _viewMode = ViewMode.Grid;

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        ViewState.OnChange += HandleViewStateChanged;
        DetermineModality();
        _viewMode = ViewState.ViewMode;
        Logs.Info("ViewerContainer initialized");
    }

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
            IDatasetItem firstItem = DatasetState.Items[0];
            _modality = firstItem.Modality;
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
        
        // Only trigger re-render if modality actually changed (new dataset loaded)
        if (_modality != previousModality)
        {
            Logs.Info($"[VIEWERCONTAINER] Modality changed from {previousModality} to {_modality}, triggering StateHasChanged");
            StateHasChanged();
        }
        else
        {
            Logs.Info($"[VIEWERCONTAINER] Modality unchanged ({_modality}), skipping StateHasChanged");
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
