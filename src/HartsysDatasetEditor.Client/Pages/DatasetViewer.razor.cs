using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MudBlazor;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Pages;

/// <summary>Main dataset viewing page with filters, viewer, and details panels.</summary>
public partial class DatasetViewer : IDisposable
{
    private const int PrefetchWindow = 120;
    [Inject] public DatasetState _datasetState { get; set; } = default!;
    [Inject] public FilterState _filterState { get; set; } = default!;
    [Inject] public ViewState _viewState { get; set; } = default!;
    [Inject] public FilterService _filterService { get; set; } = default!;
    [Inject] public DatasetCacheService _datasetCache { get; set; } = default!;
    [Inject] public NotificationService _notificationService { get; set; } = default!;
    [Inject] public NavigationService _navigationService { get; set; } = default!;

    public bool _isLoading = false;
    public string? _errorMessage = null;
    public List<IDatasetItem> _filteredItems = new();
    public int _filteredCount = 0;
    private int _lastFilteredSourceCount = 0;
    public ViewMode _viewMode = ViewMode.Grid;
    private DatasetDetailDto? _datasetDetail;
    private CancellationTokenSource? _statusPollingCts;
    private bool _isIndexedDbEnabled;
    private bool _isBuffering;
    private bool _isStatusRefreshing;

    /// <summary>Initializes component and subscribes to state changes.</summary>
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

        // Check if dataset is already loaded
        if (_datasetState.CurrentDataset != null)
        {
            ApplyFilters();
            EnsureStatusPolling();
        }

        Logs.Info("DatasetViewer page initialized");
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
            
            // Update filters WITHOUT calling StateHasChanged
            ApplyFiltersQuiet();
            
            // Prefetch more data to keep buffer full
            if (_datasetCache.HasMorePages)
            {
                int bufferTarget = _datasetState.Items.Count + PrefetchWindow;
                Logs.Info($"[DATASET STATE CHANGE] Triggering background prefetch up to {bufferTarget}");
                _ = _datasetCache.EnsureBufferedAsync(bufferTarget, CancellationToken.None);
            }
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
            _filteredItems = _filterService.ApplyFilters(_datasetState.Items, _filterState.Criteria);
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
            long loaded = Math.Min(datasetTotal, _datasetState.Items.Count);
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

    private static Severity GetStatusSeverity(IngestionStatusDto status) => status switch
    {
        IngestionStatusDto.Pending => Severity.Warning,
        IngestionStatusDto.Processing => Severity.Info,
        IngestionStatusDto.Completed => Severity.Success,
        IngestionStatusDto.Failed => Severity.Error,
        _ => Severity.Normal
    };

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
