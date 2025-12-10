using Microsoft.AspNetCore.Components;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Utilities;

namespace DatasetStudio.ClientApp.Features.Home.Pages;

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

    public void NavigateToCreateDataset()
    {
        NavigationService.NavigateTo("/datasets/create");
        Logs.Info("Navigating to create dataset from dashboard");
    }

    public void NavigateToLibrary()
    {
        NavigationService.NavigateTo("/my-datasets");
        Logs.Info("Navigating to library from dashboard");
    }

    public void NavigateToAiTools()
    {
        NavigationService.NavigateTo("/ai-tools");
        Logs.Info("Navigating to AI tools from dashboard");
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
