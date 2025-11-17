namespace HartsysDatasetEditor.Core.Models;

/// <summary>Generic paged result container</summary>
public class PagedResult<T>
{
    /// <summary>Items in this page</summary>
    public List<T> Items { get; set; } = new();
    
    /// <summary>Total count of all items</summary>
    public long TotalCount { get; set; }
    
    /// <summary>Current page number (0-based)</summary>
    public int Page { get; set; }
    
    /// <summary>Items per page</summary>
    public int PageSize { get; set; }
    
    /// <summary>Total number of pages</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    
    /// <summary>Whether there are more pages</summary>
    public bool HasNextPage => Page < TotalPages - 1;
    
    /// <summary>Whether there is a previous page</summary>
    public bool HasPreviousPage => Page > 0;
}
