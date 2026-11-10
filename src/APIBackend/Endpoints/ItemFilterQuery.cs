using System.Globalization;
using DatasetStudio.DTO.Common;
using Microsoft.Extensions.Primitives;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>
/// Parses the item filter parameters (<see cref="FilterQueryString"/>) from a request. The items
/// endpoint accepted no filter at all, so the viewer's filter panel changed nothing (audit G-2, U-6).
/// </summary>
internal static class ItemFilterQuery
{
    /// <summary>
    /// The filter in <paramref name="query"/>, or null when none is given. A malformed value is an
    /// error, not silently ignored, so a client never believes a filter applied when it didn't.
    /// </summary>
    public static (FilterRequest? Filter, string? Error) Parse(IQueryCollection query)
    {
        List<string> errors = new();

        int? ReadInt(string name)
        {
            StringValues values = query[name];
            if (StringValues.IsNullOrEmpty(values))
            {
                return null;
            }
            if (int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }
            errors.Add($"'{name}' must be a non-negative whole number");
            return null;
        }

        DateTime? ReadDate(string name)
        {
            StringValues values = query[name];
            if (StringValues.IsNullOrEmpty(values))
            {
                return null;
            }
            if (DateTime.TryParse(values[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value))
            {
                return value;
            }
            errors.Add($"'{name}' must be an ISO 8601 date");
            return null;
        }

        bool? favorites = null;
        StringValues favoritesValues = query[FilterQueryString.Favorites];
        if (!StringValues.IsNullOrEmpty(favoritesValues))
        {
            if (bool.TryParse(favoritesValues[0], out bool parsed))
            {
                favorites = parsed;
            }
            else
            {
                errors.Add($"'{FilterQueryString.Favorites}' must be true or false");
            }
        }

        FilterRequest filter = new()
        {
            SearchQuery = query[FilterQueryString.Search].FirstOrDefault()?.Trim(),
            Tags = query[FilterQueryString.Tag].Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToArray(),
            FavoritesOnly = favorites,
            MinWidth = ReadInt(FilterQueryString.MinWidth),
            MaxWidth = ReadInt(FilterQueryString.MaxWidth),
            MinHeight = ReadInt(FilterQueryString.MinHeight),
            MaxHeight = ReadInt(FilterQueryString.MaxHeight),
            DateFrom = ReadDate(FilterQueryString.DateFrom),
            DateTo = ReadDate(FilterQueryString.DateTo),
        };

        if (errors.Count > 0)
        {
            return (null, string.Join("; ", errors));
        }
        return (FilterQueryString.IsEmpty(filter) ? null : filter, null);
    }
}
