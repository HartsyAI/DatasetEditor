using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Home.Pages;

/// <summary>
/// Dashboard: the page's primary actions, the dataset open in this session, and short,
/// accurate copy about what the app does. "Getting started" used to tell users to enrich
/// items with AI tools that don't exist (audit U-25).
/// </summary>
public partial class Index : IDisposable
{
    [Inject] private NavigationService NavigationService { get; set; } = default!;
    [Inject] private DatasetState DatasetState { get; set; } = default!;

    private string? _currentDatasetName;
    private long _totalItems;
    private int _selectedItems;

    /// <summary>Reads the session's dataset and keeps the counts current.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        ReadStatistics();
    }

    private void HandleDatasetStateChanged()
    {
        ReadStatistics();
        InvokeAsync(StateHasChanged);
    }

    private void ReadStatistics()
    {
        _currentDatasetName = DatasetState.CurrentDataset?.Name;
        // The dataset's own size, not DatasetState.TotalCount (the items buffered so far), which read 0
        // for a dataset whose rows had not been paged in yet.
        _totalItems = DatasetState.CurrentDataset?.TotalItems ?? 0;
        _selectedItems = DatasetState.SelectedCount;
    }

    private void ResumeViewer()
    {
        NavigationService.NavigateToDataset();
    }

    /// <summary>Unsubscribes from dataset-state changes.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= HandleDatasetStateChanged;
    }
}
