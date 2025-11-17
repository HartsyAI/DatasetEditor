using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Pages;

/// <summary>Dashboard page displaying welcome message, quick actions, and statistics.</summary>
public partial class Index : IDisposable
{
    [Inject] public NavigationService NavigationService { get; set; } = default!;
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public AppState AppState { get; set; } = default!;

    public string? _currentDatasetName;
    public int _totalItems = 0;
    public int _selectedItems = 0;

    /// <summary>Initializes component and subscribes to state changes.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += UpdateStatistics;
        AppState.OnChange += StateHasChanged;
        UpdateStatistics();
        Logs.Info("Dashboard page initialized");
    }

    /// <summary>Updates dashboard statistics from current dataset state.</summary>
    public void UpdateStatistics()
    {
        _currentDatasetName = DatasetState.CurrentDataset?.Name;
        _totalItems = DatasetState.TotalCount;
        _selectedItems = DatasetState.SelectedCount;
        StateHasChanged();
    }

    /// <summary>Navigates to dataset viewer page for uploading new dataset.</summary>
    public void NavigateToUpload()
    {
        NavigationService.NavigateToDataset();
        Logs.Info("Navigating to upload dataset");
    }

    /// <summary>Navigates to dataset viewer page.</summary>
    public void NavigateToDatasetViewer()
    {
        NavigationService.NavigateToDataset();
        Logs.Info("Navigating to dataset viewer");
    }

    /// <summary>Navigates to settings page.</summary>
    public void NavigateToSettings()
    {
        NavigationService.NavigateToSettings();
        Logs.Info("Navigating to settings");
    }


    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= UpdateStatistics;
        AppState.OnChange -= StateHasChanged;
    }
    
    // TODO: Add recent datasets list section
    // TODO: Add usage tips or onboarding guide
    // TODO: Add keyboard shortcuts reference
    // TODO: Add performance metrics if available
}
