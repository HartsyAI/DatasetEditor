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

    /// <summary>List of filtered items to display in the viewer.</summary>
    [Parameter] public List<IDatasetItem> FilteredItems { get; set; } = new();

    /// <summary>Optional virtualized items provider for incremental loading.</summary>
    [Parameter] public ItemsProviderDelegate<IDatasetItem>? ItemsProvider { get; set; }

    /// <summary>Event callback when an item is selected.</summary>
    [Parameter] public EventCallback<IDatasetItem> OnItemSelected { get; set; }

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

    /// <summary>Updates parameters when FilteredItems changes.</summary>
    protected override void OnParametersSet()
    {
        DetermineModality();
    }

    /// <summary>Determines the modality of the current dataset.</summary>
    public void DetermineModality()
    {
        if (DatasetState.CurrentDataset != null)
        {
            _modality = DatasetState.CurrentDataset.Modality;
            Logs.Info($"Modality determined: {_modality}");
        }
        else if (FilteredItems.Count > 0)
        {
            // Infer modality from first item
            IDatasetItem firstItem = FilteredItems[0];
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
        DetermineModality();
        StateHasChanged();
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
