using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Utilities;
using DatasetModel = HartsysDatasetEditor.Core.Models.Dataset;

namespace HartsysDatasetEditor.Client.Components.Dataset;

/// <summary>Dataset file uploader component with drag-drop support and TSV parsing.</summary>
public partial class DatasetUploader
{
    [Inject] public IJSRuntime JsRuntime { get; set; } = default!;
    [Inject] public DatasetLoader DatasetLoader { get; set; } = default!;
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public NotificationService NotificationService { get; set; } = default!;
    [Inject] public NavigationService NavigationService { get; set; } = default!;

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

            // Read file content
            _uploadStatus = "Reading file...";
            Logs.Info("Opening read stream for uploaded file");

            string fileContent;
            using Stream stream = file.OpenReadStream(MaxFileSize);
            using StreamReader reader = new(stream);
            fileContent = await reader.ReadToEndAsync();
            Logs.Info("Completed reading uploaded file stream");

            if (string.IsNullOrWhiteSpace(fileContent))
            {
                throw new Exception("File is empty");
            }

            Logs.Info($"File read successfully: {fileContent.Length} characters");

            // Notify viewer once file is safely read to avoid disposing the input element mid-stream
            DatasetState.SetLoading(true);

            // Parse dataset
            _uploadStatus = "Parsing dataset...";
            StateHasChanged();

            (DatasetModel dataset, IAsyncEnumerable<IDatasetItem> itemStream) = await DatasetLoader.LoadDatasetFromTextAsync(
                fileContent,
                file.Name,
                Path.GetFileNameWithoutExtension(file.Name));

            List<IDatasetItem> items = [];
            int parsedCount = 0;

            await foreach (IDatasetItem item in itemStream)
            {
                items.Add(item);
                parsedCount++;

                // Update progress every 1000 items (roughly) to keep UI responsive.
                if (parsedCount % 1000 == 0)
                {
                    _uploadStatus = $"Parsed {parsedCount:N0} items...";
                    StateHasChanged();
                }
            }

            // Align dataset metadata with parsed results.
            dataset.TotalItems = parsedCount;
            dataset.UpdatedAt = DateTime.UtcNow;
            dataset.CreatedAt = dataset.CreatedAt == default ? DateTime.UtcNow : dataset.CreatedAt;

            // TODO: Stream items directly into DatasetState instead of materializing full list once incremental rendering is ready.

            Logs.Info($"Dataset parsed successfully: {parsedCount} items");

            // Load into state
            _uploadStatus = "Loading dataset...";
            StateHasChanged();

            DatasetState.LoadDataset(dataset, items);
            DatasetState.SetLoading(false);

            // Show success notification
            NotificationService.ShowSuccess($"Dataset loaded successfully: {items.Count:N0} items");

            // Navigate to dataset viewer
            await Task.Delay(500); // Brief delay to show success message
            NavigationService.NavigateToDataset(dataset.Id);

        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            Logs.Error("Failed to process uploaded file", ex);
            DatasetState.SetError(ex.Message);
            NotificationService.ShowError($"Upload failed: {ex.Message}");
        }
        finally
        {
            _isUploading = false;
            StateHasChanged();
        }
    }
    
    // TODO: Add file validation (check headers, sample data)
    // TODO: Add resumable upload for very large files
    // TODO: Add format detection and parser selection
    // TODO: Add preview of first few rows before full parse
    // TODO: Add drag-drop file access via JavaScript interop
}
