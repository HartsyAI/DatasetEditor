using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Items;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.Core.BusinessLogic;

/// <summary>Service for filtering dataset items based on criteria</summary>
public class FilterService
{
    // NOTE: DatasetItemDto-specific filtering removed to avoid circular dependency between Core and DTO.
    // Use the generic ApplyFilters<T> method instead, which works with any type that implements IDatasetItem.

    /// <summary>Applies filter criteria to a collection of dataset items (generic version)</summary>
    public List<T> ApplyFilters<T>(List<T> items, FilterCriteria criteria) where T : IDatasetItem
    {
        if (items == null || items.Count == 0)
        {
            return new List<T>();
        }

        if (criteria == null || !criteria.HasActiveFilters())
        {
            return items;
        }

        Logs.Info($"Applying filters to {items.Count} items");

        IEnumerable<T> filtered = items;

        // Apply search query
        if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
        {
            string query = criteria.SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(item =>
                item.Title.ToLowerInvariant().Contains(query) ||
                item.Description.ToLowerInvariant().Contains(query) ||
                item.Tags.Any(t => t.ToLowerInvariant().Contains(query))
            );
        }

        // Apply tag filters
        if (criteria.Tags.Any())
        {
            filtered = filtered.Where(item =>
                criteria.Tags.All(tag => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            );
        }

        // Apply date filters
        if (criteria.DateFrom.HasValue)
        {
            filtered = filtered.Where(item => item.CreatedAt >= criteria.DateFrom.Value);
        }

        if (criteria.DateTo.HasValue)
        {
            filtered = filtered.Where(item => item.CreatedAt <= criteria.DateTo.Value);
        }

        // Apply favorites filter
        if (criteria.FavoritesOnly.HasValue && criteria.FavoritesOnly.Value)
        {
            filtered = filtered.Where(item => item.IsFavorite);
        }

        // Apply image-specific filters
        filtered = ApplyImageFilters(filtered, criteria);

        List<T> result = filtered.ToList();
        Logs.Info($"Filtered to {result.Count} items");

        return result;
    }

    /// <summary>Applies filter criteria to a collection of dataset items (legacy)</summary>
    public List<IDatasetItem> ApplyFilters(List<IDatasetItem> items, FilterCriteria criteria)
    {
        if (items == null || items.Count == 0)
        {
            return new List<IDatasetItem>();
        }

        if (criteria == null || !criteria.HasActiveFilters())
        {
            return items;
        }

        Logs.Info($"Applying filters to {items.Count} items");

        IEnumerable<IDatasetItem> filtered = items;

        // Apply search query
        if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
        {
            string query = criteria.SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(item =>
                item.Title.ToLowerInvariant().Contains(query) ||
                item.Description.ToLowerInvariant().Contains(query) ||
                item.Tags.Any(t => t.ToLowerInvariant().Contains(query))
            );
        }

        // Apply tag filters
        if (criteria.Tags.Any())
        {
            filtered = filtered.Where(item =>
                criteria.Tags.All(tag => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            );
        }

        // Apply date filters
        if (criteria.DateFrom.HasValue)
        {
            filtered = filtered.Where(item => item.CreatedAt >= criteria.DateFrom.Value);
        }

        if (criteria.DateTo.HasValue)
        {
            filtered = filtered.Where(item => item.CreatedAt <= criteria.DateTo.Value);
        }

        // Apply favorites filter
        if (criteria.FavoritesOnly.HasValue && criteria.FavoritesOnly.Value)
        {
            filtered = filtered.Where(item => item.IsFavorite);
        }

        // Apply image-specific filters
        filtered = ApplyImageFilters(filtered, criteria);

        List<IDatasetItem> result = filtered.ToList();
        Logs.Info($"Filtered to {result.Count} items");

        return result;
    }

    /// <summary>Applies image-specific filters (dimensions, file size, format, etc.)</summary>
    private IEnumerable<T> ApplyImageFilters<T>(IEnumerable<T> items, FilterCriteria criteria)
    {
        IEnumerable<ImageItem> imageItems = items.OfType<ImageItem>();

        // Apply file size filters
        if (criteria.MinFileSizeBytes.HasValue)
        {
            imageItems = imageItems.Where(item => item.FileSizeBytes >= criteria.MinFileSizeBytes.Value);
        }

        if (criteria.MaxFileSizeBytes.HasValue)
        {
            imageItems = imageItems.Where(item => item.FileSizeBytes <= criteria.MaxFileSizeBytes.Value);
        }

        // Apply dimension filters
        if (criteria.MinWidth.HasValue)
        {
            imageItems = imageItems.Where(item => item.Width >= criteria.MinWidth.Value);
        }

        if (criteria.MaxWidth.HasValue)
        {
            imageItems = imageItems.Where(item => item.Width <= criteria.MaxWidth.Value);
        }

        if (criteria.MinHeight.HasValue)
        {
            imageItems = imageItems.Where(item => item.Height >= criteria.MinHeight.Value);
        }

        if (criteria.MaxHeight.HasValue)
        {
            imageItems = imageItems.Where(item => item.Height <= criteria.MaxHeight.Value);
        }

        // Apply aspect ratio filters
        if (criteria.MinAspectRatio.HasValue)
        {
            imageItems = imageItems.Where(item => item.AspectRatio >= criteria.MinAspectRatio.Value);
        }

        if (criteria.MaxAspectRatio.HasValue)
        {
            imageItems = imageItems.Where(item => item.AspectRatio <= criteria.MaxAspectRatio.Value);
        }

        // Apply format filters
        if (criteria.Formats.Any())
        {
            imageItems = imageItems.Where(item =>
                criteria.Formats.Contains(item.Format, StringComparer.OrdinalIgnoreCase)
            );
        }

        // Apply photographer filter
        if (!string.IsNullOrWhiteSpace(criteria.Photographer))
        {
            string photographer = criteria.Photographer.ToLowerInvariant();
            imageItems = imageItems.Where(item =>
                item.Photographer.ToLowerInvariant().Contains(photographer)
            );
        }

        // Apply location filter
        if (!string.IsNullOrWhiteSpace(criteria.Location))
        {
            string location = criteria.Location.ToLowerInvariant();
            imageItems = imageItems.Where(item =>
                item.Location.ToLowerInvariant().Contains(location)
            );
        }

        return imageItems.Cast<T>();
    }

    // TODO: Add support for sorting results
    // TODO: Add support for custom metadata filters
    // TODO: Add support for complex query logic (AND/OR combinations)
    // TODO: Add support for filter performance optimization (indexing)
}
