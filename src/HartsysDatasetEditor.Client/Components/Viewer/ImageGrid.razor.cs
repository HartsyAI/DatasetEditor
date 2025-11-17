using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Viewer;

/// <summary>Virtualized grid component with custom 2D infinite scroll for billion-scale image datasets.</summary>
/// <remarks>Uses IntersectionObserver API for smooth, flicker-free scrolling instead of Blazor's Virtualize component which doesn't support CSS Grid.</remarks>
public partial class ImageGrid : IAsyncDisposable
{
    private const int BatchSize = 50; // Load 50 images at a time
    private const int InitialLoadSize = 100; // Load 100 images initially
    private const int RootMarginPx = 500; // Trigger load 500px before reaching sentinel

    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ViewState ViewState { get; set; } = default!;
    [Inject] public DatasetCacheService DatasetCache { get; set; } = default!;

    /// <summary>Event callback when an item is selected for detail view.</summary>
    [Parameter] public EventCallback<IDatasetItem> OnItemSelected { get; set; }

    /// <summary>Event callback when more items need to be loaded from API.</summary>
    [Parameter] public EventCallback OnLoadMore { get; set; }

    public int _gridColumns = 4;
    public List<IDatasetItem> _allItems = new(); // Reference to DatasetState.Items
    public List<IDatasetItem> _visibleItems = new(); // Currently rendered items
    public int _currentIndex = 0; // Current position in _allItems
    public bool _isLoadingMore = false;
    public bool _hasMore = true;
    public int _totalItemCount = 0;
    public ElementReference _scrollContainer;
    public string _sentinelId = $"sentinel-{Guid.NewGuid():N}";
    public DotNetObjectReference<ImageGrid>? _dotNetRef;

    /// <summary>Initializes component, subscribes to state changes, and loads initial batch.</summary>
    protected override void OnInitialized()
    {
        ViewState.OnChange += HandleViewStateChanged;
        DatasetState.OnChange += HandleDatasetStateChanged;
        _gridColumns = ViewState.GridColumns;
        _allItems = DatasetState.Items;
        
        Logs.Info($"[ImageGrid] Initialized with {_gridColumns} columns, {_allItems.Count} items available");
        
        // Load initial batch immediately
        LoadNextBatch(InitialLoadSize, triggerRender: false);
        UpdateHasMoreFlag();
    }

    /// <summary>Sets up IntersectionObserver after first render.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                await JSRuntime.InvokeVoidAsync("infiniteScrollHelper.initialize", _dotNetRef, _sentinelId, RootMarginPx);
                Logs.Info("[ImageGrid] IntersectionObserver initialized");
            }
            catch (Exception ex)
            {
                Logs.Error($"[ImageGrid] Failed to initialize IntersectionObserver: {ex.Message}");
            }
        }
    }

    /// <summary>Called by JavaScript when user scrolls to bottom (sentinel becomes visible).</summary>
    [JSInvokable]
    public async Task OnScrolledToBottom()
    {
        if (_isLoadingMore || !_hasMore)
        {
            Logs.Info("[ImageGrid] Ignoring scroll event - already loading or no more items");
            return;
        }

        Logs.Info($"[ImageGrid] User scrolled to bottom, loading more items from index {_currentIndex}");
        
        _isLoadingMore = true;
        StateHasChanged(); // Show loading spinner

        // Check if we need to fetch more from API
        if (_currentIndex >= _allItems.Count && OnLoadMore.HasDelegate)
        {
            Logs.Info("[ImageGrid] Need more items from API, invoking OnLoadMore");
            await OnLoadMore.InvokeAsync();
            
            // Wait a bit for DatasetState to update
            await Task.Delay(50);
        }

        // Load next batch into visible items
        LoadNextBatch(BatchSize, triggerRender: true);
        
        _isLoadingMore = false;
        UpdateHasMoreFlag();
        StateHasChanged();
    }

    /// <summary>Loads the next batch of items from _allItems into _visibleItems.</summary>
    /// <param name="batchSize">Number of items to load.</param>
    /// <param name="triggerRender">Whether to call StateHasChanged after loading.</param>
    public void LoadNextBatch(int batchSize, bool triggerRender)
    {
        int itemsToAdd = Math.Min(batchSize, _allItems.Count - _currentIndex);
        
        if (itemsToAdd <= 0)
        {
            _hasMore = false;
            Logs.Info($"[ImageGrid] No more items to load. Total visible: {_visibleItems.Count}");
            if (triggerRender) StateHasChanged();
            return;
        }

        // Add items from _allItems to _visibleItems
        List<IDatasetItem> newItems = _allItems.GetRange(_currentIndex, itemsToAdd);
        _visibleItems.AddRange(newItems);
        _currentIndex += itemsToAdd;
        _totalItemCount = _allItems.Count;
        UpdateHasMoreFlag();

        Logs.Info($"[ImageGrid] Loaded batch: {itemsToAdd} items. Visible: {_visibleItems.Count}/{_allItems.Count}. HasMore: {_hasMore}");
        
        if (triggerRender) StateHasChanged();
    }

    /// <summary>Handles dataset state changes when items are added or filters applied.</summary>
    public void HandleDatasetStateChanged()
    {
        List<IDatasetItem> previousItems = _allItems;
        _allItems = DatasetState.Items;

        // Check if this is a filter change (list reference changed) vs items appended (same reference)
        if (previousItems != _allItems)
        {
            Logs.Info($"[ImageGrid] Filter applied or dataset changed, resetting. New count: {_allItems.Count}");
            
            // Complete reset - filters changed
            _visibleItems.Clear();
            _currentIndex = 0;
            _hasMore = true;
            _totalItemCount = _allItems.Count;
            
            // Load initial batch
            LoadNextBatch(InitialLoadSize, triggerRender: true);
        }
        else
        {
            // Items appended to same list - update total count and hasMore flag
            int previousCount = _totalItemCount;
            _totalItemCount = _allItems.Count;
            UpdateHasMoreFlag();
            
            if (_totalItemCount > previousCount)
            {
                Logs.Info($"[ImageGrid] Items appended: {_totalItemCount - previousCount} new items. Total: {_totalItemCount}");
                // Don't call StateHasChanged - we'll load them on next scroll
            }
        }
    }

    /// <summary>Handles view state changes to update grid column count.</summary>
    public void HandleViewStateChanged()
    {
        int previousColumns = _gridColumns;
        _gridColumns = ViewState.GridColumns;
        
        if (previousColumns != _gridColumns)
        {
            Logs.Info($"[ImageGrid] Grid columns changed from {previousColumns} to {_gridColumns}");
            StateHasChanged();
        }
    }

    /// <summary>Handles click event on an image card.</summary>
    public async Task HandleItemClick(IDatasetItem item)
    {
        await OnItemSelected.InvokeAsync(item);
        Logs.Info($"[ImageGrid] Image clicked: {item.Id}");
    }

    /// <summary>Handles selection toggle for an item (checkbox click).</summary>
    public void HandleToggleSelection(IDatasetItem item)
    {
        DatasetState.ToggleSelection(item);
        StateHasChanged();
    }

    /// <summary>Checks if a specific item is currently selected.</summary>
    public bool IsItemSelected(IDatasetItem item)
    {
        return DatasetState.IsSelected(item);
    }

    /// <summary>Manually trigger loading more items (useful for debugging or programmatic control).</summary>
    public async Task TriggerLoadMore()
    {
        await OnScrolledToBottom();
    }

    /// <summary>Disposes IntersectionObserver and cleans up resources.</summary>
    public async ValueTask DisposeAsync()
    {
        ViewState.OnChange -= HandleViewStateChanged;
        DatasetState.OnChange -= HandleDatasetStateChanged;

        try
        {
            await JSRuntime.InvokeVoidAsync("infiniteScrollHelper.dispose");
        }
        catch (Exception ex)
        {
            Logs.Error($"[ImageGrid] Error disposing infinite scroll helper: {ex.Message}");
        }

        _dotNetRef?.Dispose();
        
        Logs.Info("[ImageGrid] Disposed");
    }

    private void UpdateHasMoreFlag()
    {
        bool newHasMore = _currentIndex < _allItems.Count || DatasetCache.HasMorePages;
        if (_hasMore != newHasMore)
        {
            _hasMore = newHasMore;
            if (!_hasMore)
            {
                Logs.Info("[ImageGrid] All available items loaded");
            }
        }
    }
}
