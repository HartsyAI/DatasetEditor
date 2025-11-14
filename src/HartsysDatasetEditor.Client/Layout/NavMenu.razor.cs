using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Layout;

/// <summary>Navigation menu component for main application navigation and recent datasets.</summary>
public partial class NavMenu : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;

    public List<string> _recentDatasets = new();

    /// <summary>Initializes component and loads recent datasets.</summary>
    protected override void OnInitialized()
    {
        DatasetState.OnChange += StateHasChanged;
        LoadRecentDatasets();
        Logs.Info("NavMenu initialized");
    }

    /// <summary>Loads the list of recently accessed datasets from storage.</summary>
    public void LoadRecentDatasets()
    {
        // TODO: Load from LocalStorage
        // For now, use placeholder data
        _recentDatasets = new List<string>
        {
            // Will be populated from LocalStorage in future
        };
        
        // If a dataset is currently loaded, add it to recent
        if (DatasetState.CurrentDataset != null)
        {
            string datasetName = DatasetState.CurrentDataset.Name;
            if (!_recentDatasets.Contains(datasetName))
            {
                _recentDatasets.Insert(0, datasetName);
                
                // Keep only last 5 recent datasets
                if (_recentDatasets.Count > 5)
                {
                    _recentDatasets = _recentDatasets.Take(5).ToList();
                }
            }
        }
    }

    /// <summary>Generates the URL for navigating to a specific dataset.</summary>
    /// <param name="datasetName">Name of the dataset.</param>
    /// <returns>URL with dataset name as query parameter.</returns>
    public string GetDatasetUrl(string datasetName)
    {
        return $"/dataset-viewer?name={Uri.EscapeDataString(datasetName)}";
    }

    /// <summary>Unsubscribes from state changes on disposal.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= StateHasChanged;
    }
    
    // TODO: Implement recent datasets persistence in LocalStorage
    // TODO: Add "Clear Recent" option
    // TODO: Add dataset icons based on format/modality
    // TODO: Add context menu for recent items (remove, open in new tab)
}
