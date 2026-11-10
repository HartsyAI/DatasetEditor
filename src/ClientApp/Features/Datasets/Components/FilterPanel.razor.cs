using Microsoft.AspNetCore.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using System.Threading.Tasks;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>Filter panel component for applying search and filter criteria to datasets.</summary>
public partial class FilterPanel : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public FilterState FilterState { get; set; } = default!;
    [Inject] public DatasetCacheService DatasetCache { get; set; } = default!;

    /// <summary>The filters the open dataset's source can apply; the server refuses the rest.</summary>
    private FilterSupport Support => FilterMapping.SupportFor(DatasetCache.CurrentDatasetDetail?.SourceType);

    private bool _favoritesOnly;

    private string _searchQuery = string.Empty;
    private int? _minWidth = null;
    private int? _maxWidth = null;
    private int? _minHeight = null;
    private int? _maxHeight = null;
    private DateTime? _dateFrom = null;
    private DateTime? _dateTo = null;

    private List<string> _availableTags = [];
    private Dictionary<string, bool> _selectedTags = [];

    /// <summary>Initializes component and loads available filter options.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        FilterState.OnChange += HandleFilterStateChanged;
        // The panel can be re-created while a filter is active; show the filter, not empty fields.
        SyncFromFilterState();
        LoadAvailableFilters();
        Logs.Info("FilterPanel initialized");
    }

    /// <summary>Loads available filter options from current dataset.</summary>
    public void LoadAvailableFilters()
    {
        if (DatasetState.CurrentDataset == null || DatasetState.Items.Count == 0)
        {
            return;
        }

        // Extract unique tags from all items
        HashSet<string> tags = [];
        foreach (DatasetItemDto item in DatasetState.Items)
        {
            foreach (string tag in item.Tags)
            {
                tags.Add(tag);
            }
        }

        _availableTags = [.. tags.OrderBy(t => t)];
        
        // Initialize selected tags dictionary
        foreach (string tag in _availableTags)
        {
            _selectedTags[tag] = FilterState.Criteria.Tags.Contains(tag);
        }

        Logs.Info($"Loaded {_availableTags.Count} available tags for filtering");
    }

    private string? _lastDatasetId = null;
    
    /// <summary>Handles dataset state changes to refresh available filters.</summary>
    public void HandleDatasetStateChanged()
    {
        Logs.Info($"[FILTERPANEL] HandleDatasetStateChanged called, Items={DatasetState.Items.Count}, DatasetId={DatasetState.CurrentDataset?.Id}");
        
        // Only reload filters if the dataset ID actually changed (not just items appended)
        string? currentDatasetId = DatasetState.CurrentDataset?.Id;
        
        if (currentDatasetId != _lastDatasetId)
        {
            Logs.Info($"[FILTERPANEL] New dataset detected (changed from {_lastDatasetId} to {currentDatasetId}), loading available filters");
            _lastDatasetId = currentDatasetId;
            LoadAvailableFilters();
            StateHasChanged();
        }
        else
        {
            Logs.Info($"[FILTERPANEL] Same dataset, items appended, skipping filter reload and StateHasChanged");
        }
    }

    /// <summary>Handles filter state changes from external sources.</summary>
    public void HandleFilterStateChanged()
    {
        SyncFromFilterState();
        StateHasChanged();
    }

    /// <summary>Copies the current filter into the panel's fields.</summary>
    private void SyncFromFilterState()
    {
        _searchQuery = FilterState.Criteria.SearchQuery ?? string.Empty;
        _minWidth = FilterState.Criteria.MinWidth;
        _maxWidth = FilterState.Criteria.MaxWidth;
        _minHeight = FilterState.Criteria.MinHeight;
        _maxHeight = FilterState.Criteria.MaxHeight;
        _dateFrom = FilterState.Criteria.DateFrom;
        _dateTo = FilterState.Criteria.DateTo;
        _favoritesOnly = FilterState.Criteria.FavoritesOnly == true;
    }

    /// <summary>Handles the favourites-only switch.</summary>
    public void HandleFavoritesChanged(bool favoritesOnly)
    {
        _favoritesOnly = favoritesOnly;
        FilterState.SetFavoritesOnly(favoritesOnly ? true : null);
    }

    /// <summary>Handles search query changes with debounce.</summary>
    public void HandleSearchChanged(string newQuery)
    {
        FilterState.SetSearchQuery(newQuery);
        Logs.Info($"Search query updated: {newQuery}");
    }

    /// <summary>Handles tag selection changes.</summary>
    public void HandleTagChanged(string tag, bool isSelected)
    {
        _selectedTags[tag] = isSelected;

        if (isSelected)
        {
            FilterState.AddTag(tag);
        }
        else
        {
            FilterState.RemoveTag(tag);
        }
    }

    /// <summary>Handles dimension filter changes with debounce.</summary>
    public void HandleDimensionsChanged()
    {
        FilterState.SetMinWidth(_minWidth);
        FilterState.SetMaxWidth(_maxWidth);
        FilterState.SetMinHeight(_minHeight);
        FilterState.SetMaxHeight(_maxHeight);
        Logs.Info("Dimension filters updated");
    }

    /// <summary>Handles date range filter changes.</summary>
    public Task HandleDateRangeChanged((DateTime? From, DateTime? To) range)
    {
        _dateFrom = range.From;
        _dateTo = range.To;
        FilterState.SetDateRange(_dateFrom, _dateTo);
        Logs.Info($"Date range updated: {_dateFrom?.ToShortDateString()} - {_dateTo?.ToShortDateString()}");
        return Task.CompletedTask;
    }

    /// <summary>Clears all active filters.</summary>
    public void ClearAllFilters()
    {
        FilterState.ClearFilters();
        
        // Reset UI
        _searchQuery = string.Empty;
        _minWidth = null;
        _maxWidth = null;
        _minHeight = null;
        _maxHeight = null;
        _dateFrom = null;
        _dateTo = null;
        _favoritesOnly = false;
        
        foreach (string key in _selectedTags.Keys.ToList())
        {
            _selectedTags[key] = false;
        }
        
        StateHasChanged();
        Logs.Info("All filters cleared");
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= HandleDatasetStateChanged;
        FilterState.OnChange -= HandleFilterStateChanged;
        GC.SuppressFinalize(this);
    }
    
}
