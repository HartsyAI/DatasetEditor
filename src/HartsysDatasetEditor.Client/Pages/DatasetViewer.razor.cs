using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Pages;

/// <summary>Main dataset viewing page with filters, viewer, and details panels.</summary>
public partial class DatasetViewer : IDisposable
{
    [Inject] public DatasetState _datasetState { get; set; } = default!;
    [Inject] public FilterState _filterState { get; set; } = default!;
    [Inject] public ViewState _viewState { get; set; } = default!;
    [Inject] public FilterService _filterService { get; set; } = default!;
    [Inject] public NotificationService _notificationService { get; set; } = default!;
    [Inject] public NavigationService _navigationService { get; set; } = default!;

    public bool _isLoading = false;
    public string? _errorMessage = null;
    public List<IDatasetItem> _filteredItems = new();
    public int _filteredCount = 0;
    public ViewMode _viewMode = ViewMode.Grid;

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        _datasetState.OnChange += HandleDatasetStateChanged;
        _filterState.OnChange += HandleFilterStateChanged;
        _viewState.OnChange += HandleViewStateChanged;
        
        _viewMode = _viewState.ViewMode;
        
        // Check if dataset is already loaded
        if (_datasetState.CurrentDataset != null)
        {
            ApplyFilters();
        }
        
        Logs.Info("DatasetViewer page initialized");
    }

    /// <summary>Handles dataset state changes and reapplies filters.</summary>
    public void HandleDatasetStateChanged()
    {
        ApplyFilters();
        _isLoading = _datasetState.IsLoading;
        _errorMessage = _datasetState.ErrorMessage;
        StateHasChanged();
    }

    /// <summary>Handles filter state changes and reapplies filters to dataset.</summary>
    public void HandleFilterStateChanged()
    {
        ApplyFilters();
        StateHasChanged();
    }

    /// <summary>Handles view state changes and updates view mode.</summary>
    public void HandleViewStateChanged()
    {
        _viewMode = _viewState.ViewMode;
        StateHasChanged();
    }

    /// <summary>Applies current filter criteria to the dataset items.</summary>
    public void ApplyFilters()
    {
        if (_datasetState.CurrentDataset == null || _datasetState.Items.Count == 0)
        {
            _filteredItems = new List<IDatasetItem>();
            _filteredCount = 0;
            return;
        }

        _filteredItems = _filterService.ApplyFilters(
            _datasetState.Items, 
            _filterState.Criteria
        );
        
        _filteredCount = _filteredItems.Count;
        _filterState.SetFilteredCount(_filteredCount);
        
        Logs.Info($"Filters applied: {_filteredCount} items match criteria out of {_datasetState.Items.Count} total");
    }

    /// <summary>Sets the current view mode (Grid, List, Gallery).</summary>
    /// <param name="mode">View mode to set.</param>
    public void SetViewMode(ViewMode mode)
    {
        _viewState.SetViewMode(mode);
        _viewMode = mode;
        Logs.Info($"View mode changed to: {mode}");
    }

    /// <summary>Handles item selection from the viewer.</summary>
    /// <param name="item">Selected dataset item.</param>
    public void HandleItemSelected(IDatasetItem item)
    {
        _datasetState.SelectItem(item);
        
        // Show detail panel if hidden
        if (!_viewState.ShowDetailPanel)
        {
            _viewState.ToggleDetailPanel();
        }
        
        Logs.Info($"Item selected: {item.Id}");
    }

    /// <summary>Clears the current error message.</summary>
    public void ClearError()
    {
        _errorMessage = null;
        _datasetState.SetError(string.Empty);
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        _datasetState.OnChange -= HandleDatasetStateChanged;
        _filterState.OnChange -= HandleFilterStateChanged;
        _viewState.OnChange -= HandleViewStateChanged;
    }
    
    // TODO: Add keyboard shortcuts (Ctrl+F for filter, Escape to deselect)
    // TODO: Add bulk operations toolbar when items are selected
    // TODO: Add pagination controls for large datasets
    // TODO: Add export functionality
    // TODO: Add sharing/permalink generation
}
