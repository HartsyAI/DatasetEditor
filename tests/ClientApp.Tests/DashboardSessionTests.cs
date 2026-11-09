using System;
using System.Collections.Generic;
using System.Linq;
using DatasetStudio.ClientApp.Features.Home;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.DomainModels.Datasets;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.3 (dashboard): the "Current session" card shows the open dataset's own size. It used to
/// show <see cref="DatasetState.TotalCount"/>, the number of items buffered so far, so a large
/// dataset that had only paged in its first rows (or none) read as a handful of items or 0.
/// </summary>
public sealed class DashboardSessionTests
{
    /// <summary>
    /// A 70,000-item dataset with three rows buffered reports 70,000. The buffered count (3) is what
    /// the dashboard displayed before the fix, so this fails against the old rule.
    /// </summary>
    [Fact]
    public void TotalItems_IsTheDatasetSizeNotTheBufferedCount()
    {
        DatasetState state = new();
        Guid datasetId = Guid.NewGuid();
        Dataset dataset = new() { Id = datasetId.ToString(), Name = "big", TotalItems = 70_000 };
        List<DatasetItemDto> buffered = Enumerable.Range(0, 3)
            .Select(i => new DatasetItemDto { Id = Guid.NewGuid(), DatasetId = datasetId, Title = $"item {i}" })
            .ToList();
        state.LoadDataset(dataset, buffered);

        DashboardSession session = DashboardSession.From(state);

        Assert.Equal(3, state.TotalCount);
        Assert.Equal(70_000, session.TotalItems);
        Assert.Equal("big", session.DatasetName);
    }

    /// <summary>With no dataset open the card shows no name and zero counts.</summary>
    [Fact]
    public void NoDatasetOpen_ReportsNothing()
    {
        DashboardSession session = DashboardSession.From(new DatasetState());

        Assert.Null(session.DatasetName);
        Assert.Equal(0, session.TotalItems);
        Assert.Equal(0, session.SelectedItems);
    }
}
