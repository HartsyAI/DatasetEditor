using DatasetStudio.ClientApp.Services.StateManagement;

namespace DatasetStudio.ClientApp.Features.Home;

/// <summary>
/// What the dashboard's "Current session" card shows. Kept out of the page so the rule is
/// testable: the item count is the dataset's own size, not <see cref="DatasetState.TotalCount"/>,
/// which counts only the items buffered so far and read 0 for a dataset that was just opened.
/// </summary>
/// <param name="DatasetName">The open dataset's name, or null when none is open.</param>
/// <param name="TotalItems">The open dataset's total item count.</param>
/// <param name="SelectedItems">How many items are selected in the viewer.</param>
public sealed record DashboardSession(string? DatasetName, long TotalItems, int SelectedItems)
{
    /// <summary>Reads the session values from the shared dataset state.</summary>
    /// <param name="state">The client's dataset state.</param>
    public static DashboardSession From(DatasetState state)
    {
        return new DashboardSession(
            state.CurrentDataset?.Name,
            state.CurrentDataset?.TotalItems ?? 0,
            state.SelectedCount);
    }
}
