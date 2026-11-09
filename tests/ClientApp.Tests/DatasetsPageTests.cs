using System;
using System.Collections.Generic;
using System.Linq;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Shared.Presentation;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.3 (datasets page): the filter rule and the "Updated … ago" wording are shared helpers
/// rather than page code. The page's old "Only ready" switch never re-ran the filter, and its time
/// helper printed "1 day(s) ago"; DESIGN.md §5.5 fixes the wording.
/// </summary>
public sealed class DatasetsPageTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Each step of the DESIGN.md §5.5 scale, including the date fallback.</summary>
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(3 * 86400, "6 Nov 2026")]
    public void RelativeTime_FollowsTheDesignScale(int secondsAgo, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));
    }

    /// <summary>A server timestamp slightly ahead of the browser clock reads as "just now", not negative.</summary>
    [Fact]
    public void RelativeTime_FutureTimestampReadsAsJustNow()
    {
        Assert.Equal("just now", RelativeTime.Format(Now.AddMinutes(2), Now));
    }

    /// <summary>Search matches name or description, ignoring case and surrounding spaces.</summary>
    [Fact]
    public void Filter_SearchMatchesNameOrDescription()
    {
        List<DatasetSummaryDto> datasets = Sample();

        Assert.Equal(new[] { "Cats" }, Names(new LibraryFilter("  cAt ", null, null, false).Apply(datasets)));
        Assert.Equal(new[] { "Mnist" }, Names(new LibraryFilter("digits", null, null, false).Apply(datasets)));
    }

    /// <summary>"Only ready" keeps completed datasets only; the old switch was bound without re-filtering.</summary>
    [Fact]
    public void Filter_OnlyReadyKeepsCompletedDatasets()
    {
        IReadOnlyList<DatasetSummaryDto> result = new LibraryFilter(string.Empty, null, null, true).Apply(Sample());

        Assert.Equal(new[] { "Cats", "Mnist" }, Names(result));
    }

    /// <summary>Status and source filters combine, and the original order is kept.</summary>
    [Fact]
    public void Filter_StatusAndSourceCombine()
    {
        IReadOnlyList<DatasetSummaryDto> result = new LibraryFilter(string.Empty, IngestionStatusDto.Completed, DatasetSourceType.HuggingFaceStreaming, false).Apply(Sample());

        Assert.Equal(new[] { "Mnist" }, Names(result));
    }

    /// <summary>The empty filter is inactive and returns everything.</summary>
    [Fact]
    public void Filter_NoneReturnsEverything()
    {
        Assert.False(LibraryFilter.None.IsActive);
        Assert.Equal(3, LibraryFilter.None.Apply(Sample()).Count);
        Assert.True(new LibraryFilter("x", null, null, false).IsActive);
    }

    private static List<DatasetSummaryDto> Sample()
    {
        return new List<DatasetSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Cats", Description = "Pictures of cats", Status = IngestionStatusDto.Completed, SourceType = DatasetSourceType.LocalUpload },
            new() { Id = Guid.NewGuid(), Name = "Mnist", Description = "Handwritten digits", Status = IngestionStatusDto.Completed, SourceType = DatasetSourceType.HuggingFaceStreaming },
            new() { Id = Guid.NewGuid(), Name = "Pending upload", Description = null, Status = IngestionStatusDto.Pending, SourceType = DatasetSourceType.LocalUpload }
        };
    }

    private static string[] Names(IEnumerable<DatasetSummaryDto> datasets) => datasets.Select(d => d.Name).ToArray();
}
