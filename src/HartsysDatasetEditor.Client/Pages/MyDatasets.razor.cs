using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;
using System.Net.Http.Json;
using System.Text.Json;

namespace HartsysDatasetEditor.Client.Pages;

public partial class MyDatasets
{
    private List<DatasetSummaryDto> _datasets = new();
    private List<DatasetSummaryDto> _filteredDatasets = new();
    private string _searchQuery = string.Empty;
    private bool _isLoading = false;
    private IngestionStatusDto? _statusFilter = null;
    private DatasetSourceType? _sourceFilter = null;
    private bool _onlyReady = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadDatasetsAsync();
    }

    private async Task LoadDatasetsAsync()
    {
        _isLoading = true;
        
        try
        {
            // Call the new GetAllDatasets endpoint with pagination
            HttpResponseMessage response = await HttpClient.GetAsync("/api/datasets?page=0&pageSize=50");
            
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("datasets", out JsonElement datasetsElement))
                {
                    _datasets = JsonSerializer.Deserialize<List<DatasetSummaryDto>>(
                        datasetsElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                    _filteredDatasets = _datasets;
                }
            }
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
