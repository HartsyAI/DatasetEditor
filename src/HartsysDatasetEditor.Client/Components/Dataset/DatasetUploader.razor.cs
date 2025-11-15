using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.Api;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Dataset;

/// <summary>Dataset file uploader component with drag-drop support and TSV parsing.</summary>
public partial class DatasetUploader
{
    [Inject] public IJSRuntime JsRuntime { get; set; } = default!;
    [Inject] public DatasetApiClient DatasetApiClient { get; set; } = default!;
    [Inject] public DatasetCacheService DatasetCacheService { get; set; } = default!;
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public NotificationService NotificationService { get; set; } = default!;
    [Inject] public NavigationService NavigationService { get; set; } = default!;
    [Inject] public IOptions<DatasetApiOptions> DatasetApiOptions { get; set; } = default!;

    public bool _isDragging = false;
    public bool _isUploading = false;
    public string? _errorMessage = null;
    public string _uploadStatus = string.Empty;

    private const string FileInputElementId = "fileInput";

    private async Task OpenFilePickerAsync()
    {
        // TODO: Replace with dedicated InputFile component once MudBlazor exposes built-in file picker dialog helper.
        await JsRuntime.InvokeVoidAsync("interop.clickElementById", FileInputElementId);
    }

    /// <summary>Maximum file size in bytes (100MB).</summary>
    public const long MaxFileSize = 100 * 1024 * 1024;

    /// <summary>Handles drag enter event for visual feedback.</summary>
    public void HandleDragEnter()
    {
        _isDragging = true;
    }

    /// <summary>Handles drag leave event to remove visual feedback.</summary>
    public void HandleDragLeave()
    {
        _isDragging = false;
    }

    /// <summary>Handles file drop event.</summary>
    public async Task HandleDrop(DragEventArgs e)
    {
        _isDragging = false;
        // Note: Accessing files from DragEventArgs requires JavaScript interop
        // For MVP, we'll use the browse button primarily
        // TODO: Implement drag-drop file access via JS interop
        Logs.Info("File drop detected (JS interop needed for full implementation)");
    }

    /// <summary>Handles file selection via browse button.</summary>
    public async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        IBrowserFile? file = e.File;
        if (file == null)
        {
            return;
        }

        await ProcessFileAsync(file);
    }

    /// <summary>Processes the uploaded file and loads the dataset.</summary>
    public async Task ProcessFileAsync(IBrowserFile file)
    {
        _errorMessage = null;
        _isUploading = true;
        _uploadStatus = "Validating file...";
        StateHasChanged();

        try
        {
            // Validate file size
            if (file.Size > MaxFileSize)
            {
                throw new Exception($"File size exceeds maximum limit of {MaxFileSize / 1024 / 1024}MB");
            }

            // Validate file extension
            string extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension != ".tsv" && extension != ".tsv000" && extension != ".csv" && extension != ".csv000" && extension != ".txt")
            {
                throw new Exception("Invalid file format. Please upload a TSV, TSV000, CSV, or CSV000 file.");
            }

            Logs.Info($"Processing file: {file.Name} ({file.Size} bytes)");

            DatasetState.SetLoading(true);

            _uploadStatus = "Creating dataset...";
            StateHasChanged();

            string datasetName = Path.GetFileNameWithoutExtension(file.Name);
            DatasetDetailDto? dataset = await DatasetApiClient.CreateDatasetAsync(
                new CreateDatasetRequest(datasetName, $"Uploaded via UI on {DateTime.UtcNow:O}"));

            if (dataset is null)
            {
                throw new Exception("Dataset creation failed.");
            }

            Guid datasetId = dataset.Id;

            _uploadStatus = "Uploading file to API...";
            StateHasChanged();

            await using Stream stream = file.OpenReadStream(MaxFileSize);
            await DatasetApiClient.UploadDatasetAsync(datasetId, stream, file.Name, file.ContentType);

            _uploadStatus = "Loading dataset from API...";
            StateHasChanged();

            await DatasetCacheService.LoadFirstPageAsync(datasetId);

            DatasetState.SetLoading(false);

            NotificationService.ShowSuccess($"Dataset '{dataset.Name}' ingested successfully.");

            await Task.Delay(500);
            NavigationService.NavigateToDataset(datasetId.ToString());

        }
        catch (Exception ex)
        {
            string userMessage = GetFriendlyErrorMessage(ex);
            _errorMessage = userMessage;
            Logs.Error("Failed to process uploaded file", ex);
            DatasetState.SetError(userMessage);
            NotificationService.ShowError(userMessage);
        }
        finally
        {
            _isUploading = false;
            StateHasChanged();
        }
    }

    private string GetFriendlyErrorMessage(Exception ex)
    {
        if (ex is HttpRequestException || ex.Message.Contains("TypeError: Failed to fetch", StringComparison.OrdinalIgnoreCase))
        {
            string baseAddress = DatasetApiOptions.Value.BaseAddress ?? "the configured Dataset API";
            return $"Upload failed: cannot reach Dataset API at {baseAddress}. Ensure the API is running (dotnet watch run --project src/HartsysDatasetEditor.Api) and that CORS allows https://localhost:7221.";
        }

        return $"Upload failed: {ex.Message}";
    }
    
    // TODO: Add file validation (check headers, sample data)
    // TODO: Add resumable upload for very large files
    // TODO: Add format detection and parser selection
    // TODO: Add preview of first few rows before full parse
    // TODO: Add drag-drop file access via JavaScript interop
}
