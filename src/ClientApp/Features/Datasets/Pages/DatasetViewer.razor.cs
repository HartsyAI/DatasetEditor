using System;
using System.Threading;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MudBlazor;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.BusinessLogic;
using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Features.Datasets.Pages;

/// <summary>
/// The dataset viewer (<c>/datasets/{id}</c>; <c>/dataset-viewer?id=</c> still works): status band,
/// toolbar, optional filters, the virtualized items, and the detail panel. More items are fetched
/// only when the end of the list nears the viewport (<see cref="HandleLoadMoreAsync"/>, from
/// VirtualizedItemView). The page used to start another prefetch every time a page arrived, which
/// chained requests until the whole dataset (up to 100,000 items) was in memory (audit U-5).
/// </summary>
public partial class DatasetViewer : IDisposable
{
    /// <summary>The dataset id from the <c>/datasets/{id}</c> route.</summary>
    [Parameter] public Guid? DatasetId { get; set; }

    [Inject] private ILocalStorageService _localStorage { get; set; } = default!;
    [Inject] public DatasetState _datasetState { get; set; } = default!;
    [Inject] public FilterState _filterState { get; set; } = default!;
    [Inject] public ViewState _viewState { get; set; } = default!;
    [Inject] public FilterService _filterService { get; set; } = default!;
    [Inject] public DatasetCacheService _datasetCache { get; set; } = default!;
    [Inject] public NotificationService _notificationService { get; set; } = default!;
    [Inject] public NavigationService _navigationService { get; set; } = default!;
    [Inject] public IDialogService _dialogService { get; set; } = default!;

    public bool _isLoading = false;
    public string? _errorMessage = null;
    public List<DatasetItemDto> _filteredItems = new();
    public int _filteredCount = 0;
    private int _lastFilteredSourceCount = 0;
    public ViewMode _viewMode = ViewMode.Grid;
    private DatasetDetailDto? _datasetDetail;
    private CancellationTokenSource? _statusPollingCts;
    private bool _isIndexedDbEnabled;
    private bool _isBuffering;
    private bool _isStatusRefreshing;
    // The requested dataset id (route or ?id=), kept so the error state can retry the same load.
    private Guid? _requestedDatasetId;
    private bool _parametersApplied;

    /// <summary>Subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        _datasetState.OnChange += HandleDatasetStateChanged;
        _filterState.OnChange += HandleFilterStateChanged;
        _viewState.OnChange += HandleViewStateChanged;
        _datasetCache.OnDatasetDetailChanged += HandleDatasetDetailChanged;
        _datasetCache.OnBufferingStateChanged += HandleBufferingStateChanged;

        _viewMode = _viewState.ViewMode;
        _datasetDetail = _datasetCache.CurrentDatasetDetail;
        _isIndexedDbEnabled = _datasetCache.IsIndexedDbEnabled;
    }

    /// <summary>
    /// Loads the requested dataset. Runs on every parameter change because Blazor reuses this
    /// component when navigating from one <c>/datasets/{id}</c> to another.
    /// </summary>
    protected override void OnParametersSet()
    {
        Guid? requested = DatasetId;
        if (requested is null)
        {
            string? idParam = _navigationService.GetQueryParameter("id");
            if (!string.IsNullOrWhiteSpace(idParam) && Guid.TryParse(idParam, out Guid queryId))
            {
                requested = queryId;
            }
        }

        if (_parametersApplied && requested == _requestedDatasetId)
        {
            return;
        }
        _parametersApplied = true;
        _requestedDatasetId = requested;

        if (requested is Guid requestedId)
        {
            if (_datasetCache.CurrentDatasetId == requestedId && _datasetState.CurrentDataset != null)
            {
                ApplyFilters();
                EnsureStatusPolling();
            }
            else
            {
                // Fire-and-forget; DatasetState and cache events drive the UI.
                _ = LoadDatasetFromNavigationAsync(requestedId);
            }
        }
        else if (_datasetState.CurrentDataset != null)
        {
            // No id: show the dataset already open in this session.
            ApplyFilters();
            EnsureStatusPolling();
        }
    }

    // WaitForItemsAsync and SignalItemsUpdated removed - we now use RefreshDataAsync instead

    /// <summary>Handles dataset state changes and updates UI.</summary>
    public void HandleDatasetStateChanged()
    {
        _isLoading = _datasetState.IsLoading;
        _errorMessage = _datasetState.ErrorMessage;
        
        Logs.Info($"[DATASET STATE CHANGE] Items={_datasetState.Items.Count}, Loading={_isLoading}, Error={_errorMessage != null}");
        
        // When items are appended, update filtered list WITHOUT triggering parent re-render
        if (!_isLoading && _datasetState.Items.Count > _lastFilteredSourceCount)
        {
            Logs.Info($"[DATASET STATE CHANGE] Items grew from {_lastFilteredSourceCount} to {_datasetState.Items.Count}");
            
            // Update filters WITHOUT calling StateHasChanged. No prefetch here: each appended page
            // used to start the next one, chaining through the whole dataset (U-5). More pages
            // come only from HandleLoadMoreAsync, when the end of the list nears the viewport.
            ApplyFiltersQuiet();
        }
        
        // Only re-render if we're in a loading/error state that needs UI updates
        // When items are appended, Virtualize with Items parameter handles rendering automatically
        if (_isLoading || !string.IsNullOrEmpty(_errorMessage))
        {
            Logs.Info("[DATASET STATE CHANGE] Triggering StateHasChanged due to loading/error state");
            StateHasChanged();
        }
        else
        {
            Logs.Info("[DATASET STATE CHANGE] Skipping StateHasChanged - Virtualize will handle updates");
        }
    }

    /// <summary>Handles filter state changes and reapplies filters to dataset.</summary>
    public void HandleFilterStateChanged()
    {
        Logs.Info("[FILTER STATE CHANGE] User changed filters, reapplying");
        ApplyFilters(); // This calls StateHasChanged internally
    }

    /// <summary>Handles view state changes and updates view mode.</summary>
    public void HandleViewStateChanged()
    {
        _viewMode = _viewState.ViewMode;
        StateHasChanged();
    }

    /// <summary>Handles dataset detail changes published by the cache service.</summary>
    private void HandleDatasetDetailChanged()
    {
        _datasetDetail = _datasetCache.CurrentDatasetDetail;
        EnsureStatusPolling();
        InvokeAsync(StateHasChanged);
    }

    private void HandleBufferingStateChanged(bool isBuffering)
    {
        _isBuffering = isBuffering;
        // Don't re-render on buffering state changes - this happens during scroll
        // and causes flashing. The spinner is nice-to-have but not critical.
        // If we need the spinner, we can update it less frequently or use CSS animations
    }

    /// <summary>Applies filters WITHOUT triggering StateHasChanged - for smooth item appending.</summary>
    private void ApplyFiltersQuiet()
    {
        Logs.Info($"[APPLY FILTERS QUIET] Called with {_datasetState.Items.Count} items");
        
        if (!_filterState.HasActiveFilters)
        {
            // No filters: _filteredItems references DatasetState.Items directly
            // When new items are appended to DatasetState.Items, _filteredItems automatically sees them
            if (_filteredItems != _datasetState.Items)
            {
                Logs.Info("[APPLY FILTERS QUIET] Updating _filteredItems reference to DatasetState.Items");
                _filteredItems = _datasetState.Items;
            }
        }
        else
        {
            // Filters active: need to re-filter the new items
            Logs.Info("[APPLY FILTERS QUIET] Filters active, re-filtering items");
            // TODO: Implement client-side filtering logic for DatasetItemDto
            // FilterService.ApplyFilters requires IDatasetItem which DatasetItemDto doesn't implement
            _filteredItems = _datasetState.Items; // Temporarily bypass filtering
        }

        _filteredCount = _filteredItems.Count;
        _lastFilteredSourceCount = _datasetState.Items.Count;
        Logs.Info($"[APPLY FILTERS QUIET] Updated count to {_filteredCount}");
    }

    /// <summary>Applies current filter criteria to the dataset items.</summary>
    private void ApplyFilters()
    {
        ApplyFiltersQuiet();
        Logs.Info($"[APPLY FILTERS] Completed, triggering StateHasChanged");
        StateHasChanged();
    }

    /// <summary>Sets the current view mode (Grid, List, Gallery).</summary>
    /// <param name="mode">View mode to set.</param>
    public async Task SetViewMode(ViewMode mode)
    {
        _viewState.SetViewMode(mode);
        _viewMode = mode;
        // Saved like the Settings page saves it, so the viewer reopens in the mode last used.
        await _viewState.SaveToStorageAsync(_localStorage);
    }

    /// <summary>Closes the detail panel (the small-screen sheet's close button or backdrop).</summary>
    private void CloseDetailPanel()
    {
        if (_viewState.ShowDetailPanel)
        {
            _viewState.ToggleDetailPanel();
        }
    }

    /// <summary>Handles item selection from the viewer.</summary>
    /// <param name="item">Selected dataset item.</param>
    public Task HandleItemSelected(DatasetItemDto item)
    {
        _datasetState.SelectItem(item);

        if (!_viewState.ShowDetailPanel)
        {
            _viewState.ToggleDetailPanel();
        }

        Logs.Info($"Item selected: {item.Id}");
        return Task.CompletedTask;
    }

    /// <summary>Handles infinite scroll request to load more items from API.</summary>
    public async Task HandleLoadMoreAsync()
    {
        // Only load if we have more pages available and not already loading
        if (_datasetCache.HasMorePages && !_isLoading)
        {
            Logs.Info("[DatasetViewer] ImageGrid requested more items, loading next page");

            try
            {
                await _datasetCache.LoadNextPageAsync();
                // Items are automatically appended to DatasetState.Items
                // ImageGrid will detect this and render new items smoothly
            }
            catch (Exception ex)
            {
                Logs.Error($"[DatasetViewer] Error loading more items: {ex.Message}");
                _notificationService.ShowError($"Failed to load more images: {ex.Message}");
            }
        }
        else if (!_datasetCache.HasMorePages)
        {
            Logs.Info("[DatasetViewer] No more pages available to load");
        }
    }

    private async Task LoadDatasetFromNavigationAsync(Guid datasetId)
    {
        try
        {
            Logs.Info($"[DatasetViewer] Loading dataset {datasetId} from navigation");
            await _datasetCache.LoadFirstPageAsync(datasetId);
            ApplyFilters();
            EnsureStatusPolling();
        }
        catch (Exception ex)
        {
            // The cache already put the failure into DatasetState, which renders the inline ErrorState.
            // No snackbar as well: one failure is reported once (audit U-10, DESIGN.md §4.7).
            Logs.Error($"[DatasetViewer] Failed to load dataset {datasetId} from navigation: {ex.Message}");
            if (string.IsNullOrEmpty(_errorMessage))
            {
                _errorMessage = "The server didn't return this dataset.";
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    // ItemsProvider methods removed - using Items parameter for smooth infinite scroll without flicker

    private string GetItemCountLabel()
    {
        long datasetTotal = _datasetState.CurrentDataset?.TotalItems ?? 0;

        if (_filterState.HasActiveFilters)
        {
            return $"{_filteredCount:N0} filtered";
        }

        if (datasetTotal > 0)
        {
            long loadedFromStart = _datasetCache.WindowStartIndex + _datasetState.Items.Count;
            long loaded = Math.Min(datasetTotal, loadedFromStart);
            return $"{loaded:N0} / {datasetTotal:N0} items";
        }

        return $"{_filteredCount:N0} items";
    }

    /// <summary>Refreshes ingestion status immediately.</summary>
    private async Task RefreshStatusAsync()
    {
        if (_isStatusRefreshing)
        {
            return;
        }

        _isStatusRefreshing = true;
        try
        {
            await _datasetCache.RefreshDatasetStatusAsync();
        }
        finally
        {
            _isStatusRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Starts/stops polling depending on ingestion status.</summary>
    private void EnsureStatusPolling()
    {
        bool requiresPolling = _datasetDetail is { Status: IngestionStatusDto status } &&
            (status == IngestionStatusDto.Pending || status == IngestionStatusDto.Processing);

        if (requiresPolling)
        {
            if (_statusPollingCts is { IsCancellationRequested: false })
            {
                return;
            }

            _statusPollingCts?.Cancel();
            _statusPollingCts?.Dispose();
            _statusPollingCts = new CancellationTokenSource();
            _ = PollStatusAsync(_statusPollingCts.Token);
        }
        else
        {
            _statusPollingCts?.Cancel();
        }
    }

    private async Task PollStatusAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                DatasetDetailDto? detail = await _datasetCache.RefreshDatasetStatusAsync(token).ConfigureAwait(false);
                if (detail is null || detail.Status is IngestionStatusDto.Completed or IngestionStatusDto.Failed)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when user navigates away or status completes
        }
    }

    private async Task ToggleOfflineCacheAsync(bool enabled)
    {
        _isIndexedDbEnabled = enabled;
        await _datasetCache.SetIndexedDbEnabledAsync(enabled);
        StateHasChanged();

        string status = enabled ? "enabled" : "disabled";
        _notificationService.ShowInfo($"IndexedDB caching {status}.");
    }

    /// <summary>Clears the error and loads the requested dataset again.</summary>
    private async Task RetryLoadAsync()
    {
        if (_requestedDatasetId is not Guid datasetId)
        {
            return;
        }

        ClearError();
        await LoadDatasetFromNavigationAsync(datasetId);
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
        _datasetCache.OnDatasetDetailChanged -= HandleDatasetDetailChanged;
        _datasetCache.OnBufferingStateChanged -= HandleBufferingStateChanged;
        _statusPollingCts?.Cancel();
        _statusPollingCts?.Dispose();
    }
    
    // TODO: Add keyboard shortcuts (Ctrl+F for filter, Escape to deselect)
    // TODO: Add bulk operations toolbar when items are selected
    // TODO: Add pagination controls for large datasets
    // TODO: Add export functionality
    // TODO: Add sharing/permalink generation
}
