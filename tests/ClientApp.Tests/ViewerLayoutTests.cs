using System;
using System.Collections.Generic;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.4 (viewer): grid columns follow the container width (U-20) and every item from the server
/// reaches the grid (U-30). Columns used to be the Settings value regardless of width, and items
/// without image URLs were dropped, so the count label disagreed with the server.
/// </summary>
public sealed class ViewerLayoutTests
{
    /// <summary>
    /// A wide container uses the preferred count; narrower ones drop columns so cards stay at least
    /// 180 px; a phone-width container still shows one column.
    /// </summary>
    [Theory]
    [InlineData(1400, 4, 4)]
    [InlineData(1400, 8, 7)]
    [InlineData(800, 4, 4)]
    [InlineData(780, 4, 3)]
    [InlineData(500, 4, 2)]
    [InlineData(320, 4, 1)]
    [InlineData(100, 4, 1)]
    public void ColumnsFor_CapsThePreferenceByWhatFits(double width, int preferred, int expected)
    {
        Assert.Equal(expected, GridLayout.ColumnsFor(width, preferred));
    }

    /// <summary>Before the first measurement (width 0) the preference is used as-is.</summary>
    [Fact]
    public void ColumnsFor_UnmeasuredWidthUsesThePreference()
    {
        Assert.Equal(4, GridLayout.ColumnsFor(0, 4));
        Assert.Equal(1, GridLayout.ColumnsFor(0, 0));
    }

    /// <summary>Square cards: the row height is one card width plus the gap.</summary>
    [Fact]
    public void SquareRowHeight_IsCardWidthPlusGap()
    {
        // 1000 px wide, 4 columns: (1000 - 32 - 48) / 4 = 230 px cards, + 16 px gap.
        Assert.Equal(246, GridLayout.SquareRowHeight(1000, 4), precision: 3);
    }

    /// <summary>
    /// An item with neither an image nor a thumbnail URL is kept (the card shows "No image");
    /// the old mapping dropped it, so this page of three came back with two.
    /// </summary>
    [Fact]
    public void MapItems_KeepsItemsWithoutImageUrls()
    {
        Guid datasetId = Guid.NewGuid();
        List<DatasetItemDto> page = new()
        {
            new() { Id = Guid.NewGuid(), DatasetId = datasetId, ImageUrl = "/a.jpg" },
            new() { Id = Guid.NewGuid(), DatasetId = datasetId, ImageUrl = null, ThumbnailUrl = null },
            new() { Id = Guid.NewGuid(), DatasetId = datasetId, ThumbnailUrl = "/c-thumb.jpg" }
        };

        List<DatasetItemDto> mapped = DatasetCacheService.MapItems(datasetId, page);

        Assert.Equal(3, mapped.Count);
    }

    /// <summary>
    /// Signed image URLs (HuggingFace, S3) end in a query string; the format comes from the path.
    /// The detail panel showed "JPG?EXPIRES=…" before (found in the PR 2.4 browser check).
    /// </summary>
    [Theory]
    [InlineData("https://datasets-server.huggingface.co/assets/x/0.jpg?Expires=1794244209&Signature=abc", "JPG")]
    [InlineData("/api/datasets/1/files/a/b.png#frag", "PNG")]
    [InlineData("/plain/c.webp", "WEBP")]
    public void Format_IgnoresQueryAndFragment(string imageUrl, string expected)
    {
        DatasetItemDto item = new() { Id = Guid.NewGuid(), ImageUrl = imageUrl };

        Assert.Equal(expected, item.Format());
    }
}
