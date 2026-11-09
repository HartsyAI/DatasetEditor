using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// The datasets page's search and filter values, applied client-side to the loaded page of
/// datasets. A value object so the filter bar raises one change and the rule is unit-testable.
/// </summary>
/// <param name="Search">Text matched against name and description, case-insensitively.</param>
/// <param name="Status">Only this ingestion status, or any.</param>
/// <param name="Source">Only this source type, or any.</param>
/// <param name="OnlyReady">Only datasets whose ingestion completed.</param>
public sealed record LibraryFilter(string Search, IngestionStatusDto? Status, DatasetSourceType? Source, bool OnlyReady)
{
    /// <summary>No filtering.</summary>
    public static LibraryFilter None { get; } = new(string.Empty, null, null, false);

    /// <summary>Whether any filter is set.</summary>
    public bool IsActive => !string.IsNullOrWhiteSpace(Search) || Status.HasValue || Source.HasValue || OnlyReady;

    /// <summary>Returns the datasets that pass every set filter, in their original order.</summary>
    /// <param name="datasets">The loaded datasets.</param>
    public IReadOnlyList<DatasetSummaryDto> Apply(IEnumerable<DatasetSummaryDto> datasets)
    {
        IEnumerable<DatasetSummaryDto> query = datasets;

        if (!string.IsNullOrWhiteSpace(Search))
        {
            string text = Search.Trim();
            query = query.Where(d => d.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                     (d.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (Status.HasValue)
        {
            query = query.Where(d => d.Status == Status.Value);
        }

        if (Source.HasValue)
        {
            // HuggingFace and HuggingFaceDownload are the same enum value, so equality covers both.
            query = query.Where(d => d.SourceType == Source.Value);
        }

        if (OnlyReady)
        {
            query = query.Where(d => d.Status == IngestionStatusDto.Completed);
        }

        return query.ToList();
    }
}
