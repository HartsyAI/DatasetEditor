using Blazored.LocalStorage;
using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.Core.Constants;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Layout;

/// <summary>
/// Navigation menu: sign-in state, the main pages, and "Recent datasets". The recent list is kept
/// in LocalStorage and updated whenever a different dataset opens. It used to be built once at
/// mount from the dataset open at that moment (usually none) and never stored (audit U-24).
/// </summary>
public partial class NavMenu : IDisposable
{
    [Inject] private DatasetState DatasetState { get; set; } = default!;
    [Inject] private AuthApiClient AuthApiClient { get; set; } = default!;
    [Inject] private CookieAuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    private IReadOnlyList<RecentDataset> _recentDatasets = Array.Empty<RecentDataset>();
    private string? _lastRecordedId;
    private bool _registrationOpen = true;

    /// <summary>
    /// Loads the stored recent list, starts following dataset changes, and hides "Register" when
    /// the server doesn't accept registrations (audit A-1).
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        DatasetState.OnChange += HandleDatasetStateChanged;
        DTO.Auth.AuthSettingsDto? settings = await AuthApiClient.GetSettingsAsync();
        _registrationOpen = settings is not { AllowRegistration: false };
        try
        {
            _recentDatasets = await LocalStorage.GetItemAsync<List<RecentDataset>>(StorageKeys.RecentDatasets) ?? new List<RecentDataset>();
        }
        catch (Exception ex)
        {
            // Unreadable or old-format storage: start an empty list rather than failing the menu.
            Logs.Warning($"Could not read recent datasets: {ex.Message}");
            _recentDatasets = Array.Empty<RecentDataset>();
        }
        await RecordCurrentDatasetAsync();
    }

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

    private void HandleDatasetStateChanged()
    {
        InvokeAsync(RecordCurrentDatasetAsync);
    }

    /// <summary>Puts the open dataset at the top of the list and saves it, once per dataset.</summary>
    private async Task RecordCurrentDatasetAsync()
    {
        if (DatasetState.CurrentDataset is not { } dataset || dataset.Id == _lastRecordedId)
        {
            return;
        }

        _lastRecordedId = dataset.Id;
        _recentDatasets = RecentDataset.WithOpened(_recentDatasets, new RecentDataset(dataset.Id, dataset.Name));
        StateHasChanged();
        try
        {
            await LocalStorage.SetItemAsync(StorageKeys.RecentDatasets, _recentDatasets);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Could not save recent datasets: {ex.Message}");
        }
    }

    private static string GetDatasetUrl(string datasetId) => $"/datasets/{Uri.EscapeDataString(datasetId)}";

    /// <summary>Unsubscribes from dataset changes.</summary>
    public void Dispose()
    {
        DatasetState.OnChange -= HandleDatasetStateChanged;
    }
}
