using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.ClientApp.Services.ApiClients;

namespace DatasetStudio.ClientApp.Features.Datasets.Pages;

public partial class DatasetLibrary : ComponentBase
{
    [Inject] public DatasetApiClient DatasetApiClient { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public ISnackbar Snackbar { get; set; } = default!;


    private List<DatasetSummaryDto> _datasets = new();
    private List<DatasetSummaryDto> _filteredDatasets = new();
    private string _searchQuery = string.Empty;
    private bool _isLoading = false;
    private IngestionStatusDto? _statusFilter = null;
    private DatasetSourceType? _sourceFilter = null;
    private bool _onlyReady = false;

    protected override Task OnInitializedAsync()
    {
        return LoadDatasetsAsync();
    }

    private async Task LoadDatasetsAsync()
    {
        _isLoading = true;
        
        try
        {
            IReadOnlyList<DatasetSummaryDto> datasets = await DatasetApiClient.GetAllDatasetsAsync(page: 0, pageSize: 50, CancellationToken.None);
            _datasets = datasets.ToList();
            _filteredDatasets = _datasets;
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to load datasets", ex);
            Snackbar.Add("Failed to load datasets", Severity.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void HandleSearchKeyUp(KeyboardEventArgs e)
    {
        FilterDatasets();
    }

    private void FilterDatasets()
    {
        IEnumerable<DatasetSummaryDto> query = _datasets;

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            string text = _searchQuery.ToLowerInvariant();
            query = query.Where(d => d.Name.ToLowerInvariant().Contains(text) ||
                                     (d.Description?.ToLowerInvariant().Contains(text) ?? false));
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

    private void ViewDataset(DatasetSummaryDto dataset)
    {
        Navigation.NavigateTo($"/dataset-viewer?id={dataset.Id}");
    }

    private void ShowDatasetMenu(DatasetSummaryDto dataset)
    {
        // TODO: Show context menu with options (rename, delete, export, etc.)
        Snackbar.Add("Context menu coming soon", Severity.Info);
    }

    private async Task DeleteDatasetAsync(DatasetSummaryDto dataset)
    {
        try
        {
            bool success = await DatasetApiClient.DeleteDatasetAsync(dataset.Id, CancellationToken.None);
            if (!success)
            {
                Snackbar.Add($"Failed to delete dataset '{dataset.Name}'.", Severity.Error);
                return;
            }

            _datasets.RemoveAll(d => d.Id == dataset.Id);
            _filteredDatasets.RemoveAll(d => d.Id == dataset.Id);

            Snackbar.Add($"Dataset '{dataset.Name}' deleted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to delete dataset", ex);
            Snackbar.Add("Failed to delete dataset.", Severity.Error);
        }
    }

    private string GetTruncatedDescription(string description)
    {
        return description.Length > 100 
            ? description.Substring(0, 97) + "..." 
            : description;
    }

    private Color GetStatusColor(IngestionStatusDto status) => status switch
    {
        IngestionStatusDto.Pending => Color.Warning,
        IngestionStatusDto.Processing => Color.Info,
        IngestionStatusDto.Completed => Color.Success,
        IngestionStatusDto.Failed => Color.Error,
        _ => Color.Default
    };

    private string GetSourceLabel(DatasetSummaryDto dataset)
    {
        string source = dataset.SourceType switch
        {
            DatasetSourceType.LocalUpload => "Local upload",
            DatasetSourceType.HuggingFaceDownload => "HuggingFace download",
            DatasetSourceType.HuggingFaceStreaming => "HuggingFace streaming",
            DatasetSourceType.ExternalS3Streaming => "External S3 streaming",
            _ => "Unknown source"
        };

        if (dataset.IsStreaming && dataset.SourceType == DatasetSourceType.HuggingFaceDownload)
        {
            source += " (streaming)";
        }

        return source;
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

    private string FormatTimeAgo(DateTime dateTime)
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
