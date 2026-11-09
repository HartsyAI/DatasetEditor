using System;
using System.Collections.Generic;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 4.4 (audit U-6): the filter panel's criteria become a server request, and the panel offers
/// only what the dataset's source supports.
/// </summary>
public sealed class FilterMappingTests
{
    /// <summary>No criteria means no filter at all, so the viewer isn't reloaded for nothing.</summary>
    [Fact]
    public void ToRequest_EmptyCriteria_IsNull()
    {
        Assert.Null(FilterMapping.ToRequest(new FilterCriteria()));
        Assert.Null(FilterMapping.ToRequest(new FilterCriteria { SearchQuery = "   " }));
    }

    [Fact]
    public void ToRequest_CarriesEveryCriterionTheServerApplies()
    {
        FilterCriteria criteria = new()
        {
            SearchQuery = " sunset ",
            Tags = new List<string> { "cat" },
            FavoritesOnly = true,
            MinWidth = 100,
            MaxHeight = 900,
            DateFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        FilterRequest request = FilterMapping.ToRequest(criteria)!;

        Assert.Equal("sunset", request.SearchQuery);
        Assert.Equal(new[] { "cat" }, request.Tags);
        Assert.True(request.FavoritesOnly);
        Assert.Equal(100, request.MinWidth);
        Assert.Equal(900, request.MaxHeight);
        Assert.Contains("&q=sunset", FilterQueryString.Build(request));
        Assert.Contains("&tag=cat", FilterQueryString.Build(request));
    }

    [Theory]
    [InlineData(DatasetSourceType.LocalUpload, FilterSupport.All)]
    [InlineData(DatasetSourceType.LocalFolder, FilterSupport.All)]
    [InlineData(DatasetSourceType.HuggingFaceStreaming, FilterSupport.SearchOnly)]
    [InlineData(DatasetSourceType.ExternalS3Streaming, FilterSupport.None)]
    public void SupportFor_MatchesWhatTheServerCanFilter(DatasetSourceType source, FilterSupport expected)
    {
        Assert.Equal(expected, FilterMapping.SupportFor(source));
    }
}
