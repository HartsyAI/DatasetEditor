using DatasetStudio.Core.DomainModels;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Turns the filter panel's criteria into the request the items endpoint applies, and says which
/// filters a dataset's source supports. Filters used to stay in the client and change nothing
/// (audit U-6); the server now does the filtering.
/// </summary>
public static class FilterMapping
{
    /// <summary>The filter to send, or null when the criteria restrict nothing.</summary>
    public static FilterRequest? ToRequest(FilterCriteria criteria)
    {
        FilterRequest request = new()
        {
            SearchQuery = string.IsNullOrWhiteSpace(criteria.SearchQuery) ? null : criteria.SearchQuery.Trim(),
            Tags = criteria.Tags.ToArray(),
            FavoritesOnly = criteria.FavoritesOnly == true ? true : null,
            MinWidth = criteria.MinWidth,
            MaxWidth = criteria.MaxWidth,
            MinHeight = criteria.MinHeight,
            MaxHeight = criteria.MaxHeight,
            DateFrom = criteria.DateFrom,
            DateTo = EndOfDay(criteria.DateTo),
        };
        return FilterQueryString.IsEmpty(request) ? null : request;
    }

    /// <summary>
    /// The date picker gives the chosen "To" day at midnight, and the server keeps items created
    /// up to that instant, so almost everything from the chosen day was left out (and choosing the
    /// same From and To day matched nothing). A midnight bound means the whole day; an explicit
    /// time is kept.
    /// </summary>
    public static DateTime? EndOfDay(DateTime? dateTo) =>
        dateTo is DateTime to && to.TimeOfDay == TimeSpan.Zero
            ? to.AddDays(1).AddTicks(-1)
            : dateTo;

    /// <summary>What the filter panel offers for a source.</summary>
    public static FilterSupport SupportFor(DatasetSourceType? sourceType)
    {
        return sourceType switch
        {
            DatasetSourceType.HuggingFaceStreaming => FilterSupport.SearchOnly,
            DatasetSourceType.ExternalS3Streaming => FilterSupport.None,
            _ => FilterSupport.All,
        };
    }
}

/// <summary>Which filters a dataset's source can apply.</summary>
public enum FilterSupport
{
    /// <summary>Every filter (local datasets).</summary>
    All,

    /// <summary>Text search only (HuggingFace streaming, through datasets-server /search).</summary>
    SearchOnly,

    /// <summary>No filters (S3 buckets).</summary>
    None,
}
