using System.Security.Claims;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Shared.Components;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace DatasetStudio.ClientApp.Features.Datasets.Pages;

/// <summary>
/// The datasets page (<c>/datasets</c>; <c>/my-datasets</c> is kept so old links still work): the
/// caller's datasets plus public ones, filtered client-side over the loaded page. Loading,
/// failure, "no datasets" and "no matches" are distinct states.
/// </summary>
public partial class DatasetLibrary : ComponentBase
{
    [Inject] private DatasetApiClient DatasetApiClient { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }

    private List<DatasetSummaryDto> _datasets = new();
    private IReadOnlyList<DatasetSummaryDto> _filteredDatasets = Array.Empty<DatasetSummaryDto>();
    private LibraryFilter _filter = LibraryFilter.None;
    private bool _isLoading;
    private string? _loadError;
    private bool _isAuthenticated;
    private Guid? _currentUserId;

    private string PageTitleText => _isAuthenticated ? "My Datasets" : "Public Datasets";

    /// <summary>Resolves the caller and loads the first page of datasets.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            _isAuthenticated = authState.User.Identity?.IsAuthenticated ?? false;
            string? idClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _currentUserId = Guid.TryParse(idClaim, out Guid id) ? id : null;
        }

        await LoadDatasetsAsync();
    }

    // GetAllAsync's visibility filter returns public datasets from every user plus the
    // caller's own private ones -- ownership, not "did I create it", gates destructive actions.
    private bool CanDelete(DatasetSummaryDto dataset) =>
        _currentUserId is not null && dataset.CreatedByUserId == _currentUserId;

    private async Task LoadDatasetsAsync()
    {
        _isLoading = true;
        _loadError = null;

        try
        {
            IReadOnlyList<DatasetSummaryDto> datasets = await DatasetApiClient.GetAllDatasetsAsync(page: 0, pageSize: 50, CancellationToken.None);
            _datasets = datasets.ToList();
            _filteredDatasets = _filter.Apply(_datasets);
        }
        catch (Exception ex)
        {
            // Shown inline with a retry; not also as a snackbar (DESIGN.md §4.7).
            Logs.Error("Failed to load datasets", ex);
            _loadError = "The server didn't respond. Check that the API is running, then retry.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnFilterChanged(LibraryFilter filter)
    {
        _filter = filter;
        _filteredDatasets = _filter.Apply(_datasets);
    }

    private void ClearFilters() => OnFilterChanged(LibraryFilter.None);

    private void ViewDataset(DatasetSummaryDto dataset)
    {
        Navigation.NavigateTo($"/datasets/{dataset.Id}");
    }

    private async Task DeleteDatasetAsync(DatasetSummaryDto dataset)
    {
        // Deletion removes the dataset's shards and cannot be undone, so the user confirms first.
        bool confirmed = await DialogService.ConfirmAsync(
            title: "Delete dataset",
            message: $"Delete \"{dataset.Name}\"?",
            confirmLabel: "Delete",
            destructive: true,
            secondaryMessage: "Its items are removed from the server. This cannot be undone.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            bool success = await DatasetApiClient.DeleteDatasetAsync(dataset.Id, CancellationToken.None);
            if (!success)
            {
                Notifications.ShowError($"Couldn't delete \"{dataset.Name}\".");
                return;
            }

            _datasets.RemoveAll(d => d.Id == dataset.Id);
            _filteredDatasets = _filter.Apply(_datasets);
            Notifications.ShowSuccess($"Deleted \"{dataset.Name}\".");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to delete dataset", ex);
            Notifications.ShowError($"Couldn't delete \"{dataset.Name}\". The server didn't respond.");
        }
    }
}
