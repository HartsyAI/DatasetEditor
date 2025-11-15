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
    public ViewMode _viewMode = ViewMode.Grid;
    private DatasetDetailDto? _datasetDetail;
    private CancellationTokenSource? _statusPollingCts;
    private bool _isIndexedDbEnabled;
    private bool _isStatusRefreshing;

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        _datasetState.OnChange += HandleDatasetStateChanged;
        _filterState.OnChange += HandleFilterStateChanged;
        _viewState.OnChange += HandleViewStateChanged;
        _datasetCache.OnDatasetDetailChanged += HandleDatasetDetailChanged;

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

    /// <summary>Handles dataset detail changes published by the cache service.</summary>
    private void HandleDatasetDetailChanged()
    {
        _datasetDetail = _datasetCache.CurrentDatasetDetail;
        EnsureStatusPolling();
        InvokeAsync(StateHasChanged);
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

    /// <summary>Provides paged data to the Virtualize component, requesting new API pages as needed.</summary>
    private async ValueTask<ItemsProviderResult<IDatasetItem>> ProvideItemsAsync(ItemsProviderRequest request)
    {
        if (_datasetState.CurrentDataset is null)
        {
            return new ItemsProviderResult<IDatasetItem>(Array.Empty<IDatasetItem>(), 0);
        }

        int required = request.StartIndex + request.Count;
        await _datasetCache.EnsureBufferedAsync(required, request.CancellationToken).ConfigureAwait(false);

        // Ensure filters include any newly streamed items
        ApplyFilters();

        if (_filteredItems.Count <= request.StartIndex)
        {
            return new ItemsProviderResult<IDatasetItem>(Array.Empty<IDatasetItem>(), _filteredCount);
        }

        int available = Math.Min(request.Count, _filteredItems.Count - request.StartIndex);
        List<IDatasetItem> segment = _filteredItems.GetRange(request.StartIndex, available);
        return new ItemsProviderResult<IDatasetItem>(segment, _filteredCount);
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
        _statusPollingCts?.Cancel();
        _statusPollingCts?.Dispose();
    }
    
    // TODO: Add keyboard shortcuts (Ctrl+F for filter, Escape to deselect)
    // TODO: Add bulk operations toolbar when items are selected
    // TODO: Add pagination controls for large datasets
    // TODO: Add export functionality
    // TODO: Add sharing/permalink generation
}
