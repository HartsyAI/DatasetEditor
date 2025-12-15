using Microsoft.AspNetCore.Components;
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

    public string _searchQuery = string.Empty;
    public int? _minWidth = null;
    public int? _maxWidth = null;
    public int? _minHeight = null;
    public int? _maxHeight = null;
    public DateTime? _dateFrom = null;
    public DateTime? _dateTo = null;

    public List<string> _availableTags = [];
    public Dictionary<string, bool> _selectedTags = [];

    /// <summary>Initializes component and loads available filter options.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        FilterState.OnChange += HandleFilterStateChanged;
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
        // Sync UI with filter state
        _searchQuery = FilterState.Criteria.SearchQuery ?? string.Empty;
        _minWidth = FilterState.Criteria.MinWidth;
        _maxWidth = FilterState.Criteria.MaxWidth;
        _minHeight = FilterState.Criteria.MinHeight;
        _maxHeight = FilterState.Criteria.MaxHeight;
        _dateFrom = FilterState.Criteria.DateFrom;
        _dateTo = FilterState.Criteria.DateTo;
        StateHasChanged();
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
    
    // TODO: Add preset filters (e.g., "High Resolution", "Recent", "Popular")
    // TODO: Add save/load filter sets
    // TODO: Add filter history for quick recall
    // TODO: Add more filter types (photographer, color, orientation)
    // TODO: Add filter count badges showing how many items match each filter
}
