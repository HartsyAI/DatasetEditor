namespace DatasetStudio.ClientApp.Shared.Layout;

/// <summary>One entry in the navigation's "Recent datasets" list, persisted in LocalStorage.</summary>
/// <param name="Id">The dataset id (the viewer route is <c>/datasets/{Id}</c>).</param>
/// <param name="Name">The name shown in the list.</param>
public sealed record RecentDataset(string Id, string Name)
{
    /// <summary>How many recent datasets the list keeps.</summary>
    public const int MaxEntries = 5;

    /// <summary>
    /// Returns the list with <paramref name="opened"/> first: an existing entry for the same id moves
    /// to the top (taking the new name), and the list is trimmed to <see cref="MaxEntries"/>.
    /// </summary>
    /// <param name="current">The list as stored.</param>
    /// <param name="opened">The dataset just opened.</param>
    public static IReadOnlyList<RecentDataset> WithOpened(IEnumerable<RecentDataset> current, RecentDataset opened)
    {
        return new[] { opened }
            .Concat(current.Where(entry => entry.Id != opened.Id))
            .Take(MaxEntries)
            .ToList();
    }
}
