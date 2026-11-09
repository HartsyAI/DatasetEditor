using System.Collections.Generic;
using System.Linq;
using DatasetStudio.ClientApp.Shared.Layout;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.6 (audit U-24): "Recent datasets" keeps the last five opened datasets, most recent first,
/// without duplicates. It used to show only the dataset open when the menu first rendered.
/// </summary>
public sealed class RecentDatasetTests
{
    /// <summary>A newly opened dataset goes to the top.</summary>
    [Fact]
    public void WithOpened_PutsTheNewDatasetFirst()
    {
        IReadOnlyList<RecentDataset> list = RecentDataset.WithOpened(
            new[] { new RecentDataset("a", "A"), new RecentDataset("b", "B") },
            new RecentDataset("c", "C"));

        Assert.Equal(new[] { "c", "a", "b" }, list.Select(r => r.Id));
    }

    /// <summary>Reopening a dataset moves it to the top once and takes its current name.</summary>
    [Fact]
    public void WithOpened_MovesAnExistingEntryAndUpdatesItsName()
    {
        IReadOnlyList<RecentDataset> list = RecentDataset.WithOpened(
            new[] { new RecentDataset("a", "A"), new RecentDataset("b", "B old") },
            new RecentDataset("b", "B new"));

        Assert.Equal(new[] { "b", "a" }, list.Select(r => r.Id));
        Assert.Equal("B new", list[0].Name);
    }

    /// <summary>The list never grows past five entries.</summary>
    [Fact]
    public void WithOpened_KeepsAtMostFive()
    {
        IEnumerable<RecentDataset> five = Enumerable.Range(1, 5).Select(i => new RecentDataset(i.ToString(), $"D{i}"));

        IReadOnlyList<RecentDataset> list = RecentDataset.WithOpened(five, new RecentDataset("new", "New"));

        Assert.Equal(RecentDataset.MaxEntries, list.Count);
        Assert.Equal("new", list[0].Id);
        Assert.DoesNotContain(list, r => r.Id == "5");
    }
}
