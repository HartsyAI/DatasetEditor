using System.Globalization;
using System.Text;

namespace DatasetStudio.DTO.Common;

/// <summary>
/// The query-string form of a <see cref="FilterRequest"/> on <c>GET /api/datasets/{id}/items</c>:
/// <c>q</c>, repeated <c>tag</c>, <c>favorites</c>, <c>minWidth</c>, <c>maxWidth</c>,
/// <c>minHeight</c>, <c>maxHeight</c>, <c>dateFrom</c>, <c>dateTo</c> (ISO 8601). The client
/// builds it here and the API parses the same names, so the two can't drift.
/// </summary>
public static class FilterQueryString
{
    public const string Search = "q";
    public const string Tag = "tag";
    public const string Favorites = "favorites";
    public const string MinWidth = "minWidth";
    public const string MaxWidth = "maxWidth";
    public const string MinHeight = "minHeight";
    public const string MaxHeight = "maxHeight";
    public const string DateFrom = "dateFrom";
    public const string DateTo = "dateTo";

    /// <summary>Whether the filter restricts anything.</summary>
    public static bool IsEmpty(FilterRequest? filter)
    {
        return filter is null
            || (string.IsNullOrWhiteSpace(filter.SearchQuery)
                && filter.Tags.Length == 0
                && filter.FavoritesOnly != true
                && filter.MinWidth is null && filter.MaxWidth is null
                && filter.MinHeight is null && filter.MaxHeight is null
                && filter.DateFrom is null && filter.DateTo is null);
    }

    /// <summary>Whether the filter uses anything beyond a text search.</summary>
    public static bool HasNonSearchCriteria(FilterRequest filter)
    {
        return !IsEmpty(filter with { SearchQuery = null });
    }

    /// <summary>The filter as <c>&amp;name=value</c> pairs to append to a URL (empty when there is no filter).</summary>
    public static string Build(FilterRequest? filter)
    {
        if (IsEmpty(filter))
        {
            return string.Empty;
        }

        StringBuilder query = new();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            }
        }

        Add(Search, filter!.SearchQuery?.Trim());
        foreach (string tag in filter.Tags)
        {
            Add(Tag, tag);
        }
        Add(Favorites, filter.FavoritesOnly == true ? "true" : null);
        Add(MinWidth, filter.MinWidth?.ToString(CultureInfo.InvariantCulture));
        Add(MaxWidth, filter.MaxWidth?.ToString(CultureInfo.InvariantCulture));
        Add(MinHeight, filter.MinHeight?.ToString(CultureInfo.InvariantCulture));
        Add(MaxHeight, filter.MaxHeight?.ToString(CultureInfo.InvariantCulture));
        Add(DateFrom, filter.DateFrom?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Add(DateTo, filter.DateTo?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return query.ToString();
    }
}
