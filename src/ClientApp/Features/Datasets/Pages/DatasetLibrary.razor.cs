using System.Security.Claims;
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
/// The datasets page: the caller's datasets plus public ones, with client-side search and
/// filters over the loaded page. Loading, failure and "no datasets" are distinct states; a
/// failed request used to render "No datasets yet" plus a snackbar (audit U-10).
/// </summary>
public partial class DatasetLibrary : ComponentBase
{
    [Inject] private DatasetApiClient DatasetApiClient { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }

    private List<DatasetSummaryDto> _datasets = new();
    private List<DatasetSummaryDto> _filteredDatasets = new();
    private string _searchQuery = string.Empty;
    private bool _isLoading;
    private string? _loadError;
    private IngestionStatusDto? _statusFilter;
    private DatasetSourceType? _sourceFilter;
    private bool _onlyReady;
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
            FilterDatasets();
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

    private void FilterDatasets()
    {
        IEnumerable<DatasetSummaryDto> query = _datasets;

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            string text = _searchQuery.Trim();
            query = query.Where(d => d.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                     (d.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (_statusFilter.HasValue)
        {
            query = query.Where(d => d.Status == _statusFilter.Value);
        }

        if (_sourceFilter.HasValue)
        {
            query = query.Where(d => d.SourceType == _sourceFilter.Value);
        }

        if (_onlyReady)
        {
            query = query.Where(d => d.Status == IngestionStatusDto.Completed);
        }

        _filteredDatasets = query.ToList();
    }

    private void OnStatusFilterChanged(IngestionStatusDto? value)
    {
        _statusFilter = value;
        FilterDatasets();
    }

    private void OnSourceFilterChanged(DatasetSourceType? value)
    {
        _sourceFilter = value;
        FilterDatasets();
    }

    // The switch used to @bind the field directly, so toggling it never re-ran the filter (U-6).
    private void OnOnlyReadyChanged(bool value)
    {
        _onlyReady = value;
        FilterDatasets();
    }

    private void ClearFilters()
    {
        _searchQuery = string.Empty;
        _statusFilter = null;
        _sourceFilter = null;
        _onlyReady = false;
        FilterDatasets();
    }

    private void ViewDataset(DatasetSummaryDto dataset)
    {
        Navigation.NavigateTo($"/dataset-viewer?id={dataset.Id}");
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
            FilterDatasets();
            Notifications.ShowSuccess($"Deleted \"{dataset.Name}\".");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to delete dataset", ex);
            Notifications.ShowError($"Couldn't delete \"{dataset.Name}\". The server didn't respond.");
        }
    }

    private static string GetTruncatedDescription(string description)
    {
        return description.Length > 100
            ? description.Substring(0, 97) + "..."
            : description;
    }

    private static string FormatTimeAgo(DateTime dateTime)
    {
        TimeSpan span = DateTime.UtcNow - dateTime;

        if (span.TotalDays > 365)
            return $"{(int)(span.TotalDays / 365)} year(s) ago";
        if (span.TotalDays > 30)
            return $"{(int)(span.TotalDays / 30)} month(s) ago";
        if (span.TotalDays > 1)
            return $"{(int)span.TotalDays} day(s) ago";
        if (span.TotalHours > 1)
            return $"{(int)span.TotalHours} hour(s) ago";
        if (span.TotalMinutes > 1)
            return $"{(int)span.TotalMinutes} minute(s) ago";

        return "just now";
    }
}
