using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Services.StateManagement;

/// <summary>Manages active filter criteria and filtered result counts.</summary>
public class FilterState
{
    /// <summary>Current filter criteria applied to the dataset.</summary>
    public FilterCriteria Criteria { get; private set; } = new();
    
    /// <summary>Count of items after filters are applied.</summary>
    public int FilteredCount { get; private set; }
    
    /// <summary>Indicates whether any filters are currently active.</summary>
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Criteria.SearchQuery) ||
                                     Criteria.Tags.Count > 0 ||
                                     Criteria.DateFrom.HasValue ||
                                     Criteria.DateTo.HasValue ||
                                     Criteria.MinWidth.HasValue ||
                                     Criteria.MaxWidth.HasValue ||
                                     Criteria.MinHeight.HasValue ||
                                     Criteria.MaxHeight.HasValue ||
                                     Criteria.FavoritesOnly == true;
    
    /// <summary>Event fired when filter criteria changes.</summary>
    public event Action? OnChange;
    
    /// <summary>Clears all active filters, resetting to default state.</summary>
    public void ClearFilters()
    {
        Criteria = new FilterCriteria();
        FilteredCount = 0;
        NotifyStateChanged();
        Logs.Info("All filters cleared");
    }
    
    /// <summary>Sets the search query for text-based filtering.</summary>
    /// <param name="query">Search query string.</param>
    public void SetSearchQuery(string query)
    {
        Criteria.SearchQuery = query;
        NotifyStateChanged();
        Logs.Info($"Search query set: {query}");
    }
    
    /// <summary>Adds a tag to the filter criteria if not already present.</summary>
    /// <param name="tag">Tag to add to filters.</param>
    public void AddTag(string tag)
    {
        if (!Criteria.Tags.Contains(tag))
        {
            Criteria.Tags.Add(tag);
            NotifyStateChanged();
            Logs.Info($"Tag added to filter: {tag}");
        }
    }
    
    /// <summary>Removes a tag from the filter criteria.</summary>
    /// <param name="tag">Tag to remove from filters.</param>
    public void RemoveTag(string tag)
    {
        if (Criteria.Tags.Remove(tag))
        {
            NotifyStateChanged();
            Logs.Info($"Tag removed from filter: {tag}");
        }
    }
    
    /// <summary>Sets the date range filter.</summary>
    /// <param name="dateFrom">Start date (inclusive), null for no lower bound.</param>
    /// <param name="dateTo">End date (inclusive), null for no upper bound.</param>
    public void SetDateRange(DateTime? dateFrom, DateTime? dateTo)
    {
        Criteria.DateFrom = dateFrom;
        Criteria.DateTo = dateTo;
        NotifyStateChanged();
        Logs.Info($"Date range filter set: {dateFrom?.ToShortDateString() ?? "none"} to {dateTo?.ToShortDateString() ?? "none"}");
    }
    
    /// <summary>Sets the minimum width filter for images.</summary>
    /// <param name="minWidth">Minimum width in pixels.</param>
    /// <summary>Shows only favourites when true; null shows everything.</summary>
    public void SetFavoritesOnly(bool? favoritesOnly)
    {
        Criteria.FavoritesOnly = favoritesOnly;
        NotifyStateChanged();
    }

    public void SetMinWidth(int? minWidth)
    {
        Criteria.MinWidth = minWidth;
        NotifyStateChanged();
        Logs.Info($"Min width filter set: {minWidth}");
    }
    
    /// <summary>Sets the maximum width filter for images.</summary>
    /// <param name="maxWidth">Maximum width in pixels.</param>
    public void SetMaxWidth(int? maxWidth)
    {
        Criteria.MaxWidth = maxWidth;
        NotifyStateChanged();
        Logs.Info($"Max width filter set: {maxWidth}");
    }
    
    /// <summary>Sets the minimum height filter for images.</summary>
    /// <param name="minHeight">Minimum height in pixels.</param>
    public void SetMinHeight(int? minHeight)
    {
        Criteria.MinHeight = minHeight;
        NotifyStateChanged();
        Logs.Info($"Min height filter set: {minHeight}");
    }
    
    /// <summary>Sets the maximum height filter for images.</summary>
    /// <param name="maxHeight">Maximum height in pixels.</param>
    public void SetMaxHeight(int? maxHeight)
    {
        Criteria.MaxHeight = maxHeight;
        NotifyStateChanged();
        Logs.Info($"Max height filter set: {maxHeight}");
    }
    
    /// <summary>Notifies all subscribers that the filter state has changed.</summary>
    protected void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
    
    // TODO: Add preset filter templates (e.g., "Portraits", "Landscapes", "High Resolution")
    // TODO: Add saved filter sets for quick recall
    // TODO: Add filter history for undo/redo
}
