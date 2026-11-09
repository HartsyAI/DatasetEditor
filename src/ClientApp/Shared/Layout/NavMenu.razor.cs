using Microsoft.AspNetCore.Components;
using DatasetStudio.ClientApp.Shared.Services;
using MudBlazor;
using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Shared.Layout;

/// <summary>Navigation menu component for main application navigation and recent datasets.</summary>
public partial class NavMenu : IDisposable
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] private AuthApiClient AuthApiClient { get; set; } = default!;
    [Inject] private CookieAuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;

    private async Task LogoutAsync()
    {
        try
        {
            await AuthApiClient.LogoutAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // The server may still hold the session, so do not report a sign-out that did not
            // happen (audit U-28). The user can retry.
            Logs.Warning($"Logout request did not complete: {ex.GetType().Name}");
            Notifications.ShowError("Could not sign out because the server is unreachable. Try again.");
            return;
        }

        AuthStateProvider.NotifyUserLoggedOut();
        Navigation.NavigateTo("/", forceLoad: false);
    }

    public List<(string Id, string Name)> _recentDatasets = new();

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
        _recentDatasets = new List<(string Id, string Name)>
        {
            // Will be populated from LocalStorage in future
        };
        
        // If a dataset is currently loaded, add it to recent
        if (DatasetState.CurrentDataset != null)
        {
            // Keep the id with the name: the viewer loads by id, so a name-only link cannot open it.
            (string Id, string Name) current = (DatasetState.CurrentDataset.Id, DatasetState.CurrentDataset.Name);
            if (!_recentDatasets.Contains(current))
            {
                _recentDatasets.Insert(0, current);
                
                // Keep only last 5 recent datasets
                if (_recentDatasets.Count > 5)
                {
                    _recentDatasets = _recentDatasets.Take(5).ToList();
                }
            }
        }
    }

    /// <summary>Generates the URL for navigating to a specific dataset.</summary>
    /// <param name="datasetId">Id of the dataset. The viewer reads only the <c>id</c> query parameter.</param>
    /// <returns>URL with the dataset id as query parameter.</returns>
    public string GetDatasetUrl(string datasetId)
    {
        return $"/datasets/{Uri.EscapeDataString(datasetId)}";
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
