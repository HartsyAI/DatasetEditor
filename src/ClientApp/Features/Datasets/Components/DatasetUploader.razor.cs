using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using MudBlazor;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.BusinessLogic;
using DatasetStudio.Core.Utilities;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

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
    [Inject] public IDialogService DialogService { get; set; } = default!;

    public bool _isDragging = false;
    public bool _isUploading = false;
    public string? _errorMessage = null;
    public string _uploadStatus = string.Empty;
    public int _uploadProgress = 0;
    public string _estimatedTimeRemaining = string.Empty;
    public string _fileInputKey = Guid.NewGuid().ToString();
    public List<IBrowserFile> _selectedFiles = new();
    public DatasetFileCollection? _detectedCollection = null;
    private DateTime _uploadStartTime;

    // Tab management
    public int _activeTabIndex = 0;
    [Parameter] public int InitialTabIndex { get; set; } = 0;

    // HuggingFace import fields
    public string _hfRepository = string.Empty;
    public string? _hfDatasetName = null;
    public string? _hfDescription = null;
    public string? _hfRevision = null;
    public string? _hfAccessToken = null;
    public bool _hfIsStreaming = false;
    public HuggingFaceDiscoveryResponse? _hfDiscoveryResponse = null;
    public bool _hfShowOptions = false;
    public bool _hfDiscovering = false;

    private const string FileInputElementId = "fileInput";

    protected override void OnInitialized()
    {
        _activeTabIndex = InitialTabIndex;
    }

    private async Task OpenFilePickerAsync()
    {
        // TODO: Replace with dedicated InputFile component once MudBlazor exposes built-in file picker dialog helper.
        await JsRuntime.InvokeVoidAsync("interop.clickElementById", FileInputElementId);
    }

    /// <summary>Maximum file size in bytes (5GB). For datasets larger than 5GB, use server-side file path upload.</summary>
    public const long MaxFileSize = 5L * 1024 * 1024 * 1024;

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
    public void HandleDrop(DragEventArgs e)
    {
        _isDragging = false;
        // Note: Accessing files from DragEventArgs requires JavaScript interop
        // For MVP, we'll use the browse button primarily
        // TODO: Implement drag-drop file access via JS interop
        Logs.Info("File drop detected (JS interop needed for full implementation)");
    }

    /// <summary>Handles multiple file selection via browse button.</summary>
    public async Task HandleFilesSelected(InputFileChangeEventArgs e)
    {
        _selectedFiles = e.GetMultipleFiles(10).ToList();
        
        if (!_selectedFiles.Any())
        {
            return;
        }
        
        // Read file contents for detection
        await DetectFileTypesAsync();
        
        StateHasChanged();
    }
    
    /// <summary>Detects file types and enrichment relationships.</summary>
    public async Task DetectFileTypesAsync()
    {
        _uploadStatus = "Analyzing files...";
        _uploadProgress = 0;
        await InvokeAsync(StateHasChanged);
        
        // Check if any file is a ZIP
        bool hasZipFile = _selectedFiles.Any(f => Path.GetExtension(f.Name).Equals(".zip", StringComparison.OrdinalIgnoreCase));
        
        if (hasZipFile)
        {
            // ZIP files need extraction, not text analysis
            // Show a message and let user click Upload to extract
            _uploadStatus = "ZIP file detected - click Upload to extract and process";
            Logs.Info($"ZIP file detected: {_selectedFiles.First(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Name}");
            
            // Create a placeholder collection for ZIP
            _detectedCollection = new DatasetFileCollection
            {
                PrimaryFileName = _selectedFiles.First(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Name,
                TotalSizeBytes = _selectedFiles.Sum(f => f.Size)
            };
            
            await InvokeAsync(StateHasChanged);
            return;
        }
        
        Dictionary<string, string> fileContents = new();
        int fileIndex = 0;
        
        foreach (IBrowserFile file in _selectedFiles)
        {
            fileIndex++;
            _uploadStatus = $"Reading file {fileIndex}/{_selectedFiles.Count}: {file.Name}...";
            _uploadProgress = (fileIndex * 50) / _selectedFiles.Count; // 0-50% for reading
            await InvokeAsync(StateHasChanged);
            
            if (file.Size > MaxFileSize)
            {
                Logs.Error($"File {file.Name} is too large (max {MaxFileSize / 1024 / 1024 / 1024}GB)");
                continue;
            }
            
            try
            {
                // For large files, read in chunks to show progress
                using Stream stream = file.OpenReadStream(MaxFileSize);
                using StreamReader reader = new(stream);
                string content = await reader.ReadToEndAsync();
                
                fileContents[file.Name] = content;
            }
            catch (JSException ex) when (ex.Message.Contains("_blazorFilesById"))
            {
                // Blazor file input reference was lost (component navigated away or disposed)
                Logs.Error($"File input reference lost while reading {file.Name}. Please try uploading again.");
                _uploadStatus = "Upload cancelled - file reference lost. Please select files again.";
                _uploadProgress = 0;
                _selectedFiles.Clear();
                await InvokeAsync(StateHasChanged);
                return;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to read file {file.Name}: {ex.Message}");
                _uploadStatus = $"Failed to read {file.Name}";
                continue;
            }
        }
        
        _uploadStatus = "Analyzing file structure...";
        _uploadProgress = 60;
        await InvokeAsync(StateHasChanged);
        
        // Detect file types
        MultiFileDetectorService detector = new();
        _detectedCollection = detector.AnalyzeFiles(fileContents);
        
        _uploadStatus = "Analysis complete";
        _uploadProgress = 100;
        await InvokeAsync(StateHasChanged);
    }
    
    /// <summary>Gets file type label for display.</summary>
    public string GetFileTypeLabel(string fileName)
    {
        if (_detectedCollection == null)
            return "Unknown";
        
        if (fileName == _detectedCollection.PrimaryFileName)
            return "Primary Dataset";
        
        EnrichmentFile? enrichment = _detectedCollection.EnrichmentFiles
            .FirstOrDefault(e => e.FileName == fileName);
        
        return enrichment != null 
            ? $"Enrichment ({enrichment.Info.EnrichmentType})" 
            : "Unknown";
    }
    
    /// <summary>Formats file size for display.</summary>
    public string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>Processes the uploaded file and loads the dataset.</summary>
    public async Task ProcessFileAsync(IBrowserFile file)
    {
        _errorMessage = null;
        _isUploading = true;
        _uploadStatus = "Validating file...";

        MemoryStream? uploadBuffer = null;

        try
        {
            // Validate file size
            if (file.Size > MaxFileSize)
            {
                throw new Exception($"File size exceeds maximum limit of {MaxFileSize / 1024 / 1024 / 1024}GB. For larger datasets, use server-side file upload.");
            }

            // Validate file extension
            string extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension != ".tsv" && extension != ".tsv000" && extension != ".csv" && extension != ".csv000" && extension != ".txt")
            {
                throw new Exception("Invalid file format. Please upload a TSV, TSV000, CSV, or CSV000 file.");
            }

            Logs.Info($"Processing file: {file.Name} ({file.Size} bytes)");

            uploadBuffer = new MemoryStream((int)Math.Min(file.Size, MaxFileSize));
            await using (Stream browserStream = file.OpenReadStream(MaxFileSize))
            {
                await browserStream.CopyToAsync(uploadBuffer);
            }
            uploadBuffer.Position = 0;

            DatasetState.SetLoading(true);

            _uploadStatus = "Creating dataset...";
            await InvokeAsync(StateHasChanged);

            string datasetName = Path.GetFileNameWithoutExtension(file.Name);
            DatasetDetailDto? dataset = await DatasetApiClient.CreateDatasetAsync(
                new CreateDatasetRequest(datasetName, $"Uploaded via UI on {DateTime.UtcNow:O}"));

            if (dataset is null)
            {
                throw new Exception("Dataset creation failed.");
            }

            Guid datasetId = dataset.Id;

            _uploadStatus = "Uploading file to API...";
            await InvokeAsync(StateHasChanged);

            uploadBuffer.Position = 0;
            await DatasetApiClient.UploadDatasetAsync(datasetId, uploadBuffer, file.Name, file.ContentType);

            _uploadStatus = "Loading dataset from API...";
            await InvokeAsync(StateHasChanged);

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
            await InvokeAsync(StateHasChanged);
            ResetFileInput();
            uploadBuffer?.Dispose();
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

    private void ResetFileInput()
    {
        _fileInputKey = Guid.NewGuid().ToString();
    }
    
    /// <summary>Handles upload of detected file collection (primary + enrichments).</summary>
    public async Task UploadDetectedCollectionAsync()
    {
        if (_detectedCollection == null || _selectedFiles.Count == 0)
        {
            _errorMessage = "No files selected for upload.";
            return;
        }
        
        _errorMessage = null;
        _isUploading = true;
        _uploadProgress = 0;
        _uploadStartTime = DateTime.UtcNow;
        _uploadStatus = "Preparing upload...";
        await InvokeAsync(StateHasChanged);
        
        List<(string fileName, Stream content)> filesToUpload = new();
        
        try
        {
            // Step 1: Extract/prepare files
            UpdateProgress(5, "Preparing files...");
            
            for (int i = 0; i < _selectedFiles.Count; i++)
            {
                IBrowserFile file = _selectedFiles[i];
                string extension = Path.GetExtension(file.Name).ToLowerInvariant();
                
                if (extension == ".zip")
                {
                    // DON'T extract ZIP in browser (causes out of memory)
                    // Upload ZIP directly to server and let it handle extraction
                    UpdateProgress(10, $"Preparing ZIP file for upload: {file.Name} ({FormatFileSize(file.Size)})...");
                    
                    using Stream browserStream = file.OpenReadStream(MaxFileSize);
                    MemoryStream zipBuffer = new((int)Math.Min(file.Size, int.MaxValue));
                    
                    // Read ZIP in chunks to show progress
                    byte[] buffer = new byte[81920]; // 80 KB chunks
                    long totalBytes = file.Size;
                    long bytesRead = 0;
                    int readCount;
                    
                    while ((readCount = await browserStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await zipBuffer.WriteAsync(buffer, 0, readCount);
                        bytesRead += readCount;
                        
                        // Update progress (10-20% for reading ZIP)
                        int progress = 10 + (int)((bytesRead * 10) / totalBytes);
                        UpdateProgress(progress, $"Reading ZIP: {FormatFileSize(bytesRead)}/{FormatFileSize(totalBytes)}...");
                    }
                    
                    zipBuffer.Position = 0;
                    
                    // Add ZIP as-is to upload (server will extract it)
                    filesToUpload.Add((file.Name, zipBuffer));
                    
                    Logs.Info($"ZIP file ready for upload: {file.Name} ({FormatFileSize(file.Size)})");
                }
                else
                {
                    UpdateProgress(10 + (i * 10 / _selectedFiles.Count), $"Reading: {file.Name}...");
                    
                    // Regular file - read into memory
                    MemoryStream ms = new();
                    using (Stream browserStream = file.OpenReadStream(MaxFileSize))
                    {
                        await browserStream.CopyToAsync(ms);
                    }
                    ms.Position = 0;
                    filesToUpload.Add((file.Name, ms));
                }
            }
            
            // Step 2: Handle multi-part files
            UpdateProgress(20, "Detecting multi-part files...");
            List<string> fileNames = filesToUpload.Select(f => f.fileName).ToList();
            Dictionary<string, List<string>> multiPartGroups = ZipHelpers.DetectMultiPartFiles(fileNames);
            
            if (multiPartGroups.Any())
            {
                Logs.Info($"Found {multiPartGroups.Count} multi-part file groups");
                UpdateProgress(25, "Merging multi-part files...");
                
                List<(string fileName, Stream content)> merged = new();
                
                foreach (var group in multiPartGroups)
                {
                    // Find all parts - use FirstOrDefault to avoid exceptions
                    List<(string, Stream)> parts = new();
                    foreach (string partName in group.Value)
                    {
                        var part = filesToUpload.FirstOrDefault(f => f.fileName == partName);
                        if (part.content != null)
                        {
                            parts.Add(part);
                        }
                        else
                        {
                            Logs.Warning($"Multi-part file not found in upload list: {partName}");
                        }
                    }
                    
                    if (parts.Count == 0)
                    {
                        Logs.Warning($"No parts found for multi-part group: {group.Key}");
                        continue;
                    }
                    
                    Logs.Info($"Merging {parts.Count} parts for {group.Key}");
                    MemoryStream mergedStream = await ZipHelpers.MergePartFilesAsync(parts, skipHeadersAfterFirst: true);
                    merged.Add((group.Key, mergedStream));
                    
                    // Remove individual parts
                    foreach (var part in parts)
                    {
                        filesToUpload.Remove(part);
                        part.Item2.Dispose();
                    }
                }
                
                filesToUpload.AddRange(merged);
                Logs.Info($"Merged into {merged.Count} complete files");
                
                // Update primary file name if it was merged
                if (merged.Any(m => _detectedCollection.PrimaryFileName.StartsWith(Path.GetFileNameWithoutExtension(m.fileName))))
                {
                    string oldPrimaryName = _detectedCollection.PrimaryFileName;
                    string newPrimaryName = merged.First(m => oldPrimaryName.StartsWith(Path.GetFileNameWithoutExtension(m.fileName))).fileName;
                    _detectedCollection.PrimaryFileName = newPrimaryName;
                    Logs.Info($"Updated primary file name from '{oldPrimaryName}' to '{newPrimaryName}' after merge");
                }
            }
            
            // Step 3: Create dataset
            UpdateProgress(30, "Creating dataset...");
            string datasetName = Path.GetFileNameWithoutExtension(_detectedCollection.PrimaryFileName);
            
            DatasetDetailDto? dataset = await DatasetApiClient.CreateDatasetAsync(
                new CreateDatasetRequest(datasetName, $"Uploaded via UI on {DateTime.UtcNow:O}"));
            
            if (dataset == null)
            {
                throw new Exception("Failed to create dataset on server.");
            }
            
            Guid datasetId = dataset.Id;
            Logs.Info($"Dataset created with ID: {datasetId}");
            
            // Step 4: Upload primary file
            UpdateProgress(40, $"Uploading primary file...");
            
            // Try to find the primary file with multiple matching strategies
            var primaryFile = filesToUpload.FirstOrDefault(f => 
                f.fileName == _detectedCollection.PrimaryFileName ||
                f.fileName.StartsWith(Path.GetFileNameWithoutExtension(_detectedCollection.PrimaryFileName)) ||
                Path.GetFileNameWithoutExtension(f.fileName) == Path.GetFileNameWithoutExtension(_detectedCollection.PrimaryFileName));
                
            if (primaryFile.content == null)
            {
                // Log available files for debugging
                Logs.Error($"Primary file '{_detectedCollection.PrimaryFileName}' not found. Available files: {string.Join(", ", filesToUpload.Select(f => f.fileName))}");
                throw new Exception($"Primary file not found: {_detectedCollection.PrimaryFileName}. Available files: {string.Join(", ", filesToUpload.Select(f => f.fileName))}");
            }
            
            primaryFile.content.Position = 0;
            await DatasetApiClient.UploadDatasetAsync(datasetId, primaryFile.content, primaryFile.fileName, "text/csv");
            
            Logs.Info($"Primary file uploaded: {primaryFile.fileName}");
            
            // Step 5: Upload enrichment files
            if (_detectedCollection.EnrichmentFiles.Any())
            {
                int enrichmentCount = _detectedCollection.EnrichmentFiles.Count;
                for (int i = 0; i < enrichmentCount; i++)
                {
                    var enrichment = _detectedCollection.EnrichmentFiles[i];
                    UpdateProgress(50 + (i * 20 / enrichmentCount), $"Uploading enrichment: {enrichment.FileName}...");
                    
                    var enrichmentFile = filesToUpload.FirstOrDefault(f => f.fileName == enrichment.FileName);
                    if (enrichmentFile.content != null)
                    {
                        enrichmentFile.content.Position = 0;
                        // TODO: Add enrichment upload endpoint
                        Logs.Info($"Enrichment file ready: {enrichment.FileName} ({enrichment.Info.EnrichmentType})");
                    }
                }
            }
            
            // Step 6: Load dataset into viewer
            UpdateProgress(70, "Loading dataset...");
            
            DatasetState.SetLoading(true);
            await DatasetCacheService.LoadFirstPageAsync(datasetId);
            DatasetState.SetLoading(false);
            
            UpdateProgress(100, "Complete!");
            
            NotificationService.ShowSuccess($"Dataset '{dataset.Name}' uploaded successfully!");
            await Task.Delay(500);
            NavigationService.NavigateToDataset(datasetId.ToString());
        }
        catch (Exception ex)
        {
            string userMessage = GetFriendlyErrorMessage(ex);
            _errorMessage = userMessage;
            Logs.Error("Failed to upload dataset collection", ex);
            DatasetState.SetError(userMessage);
            NotificationService.ShowError(userMessage);
        }
        finally
        {
            // Cleanup
            foreach (var file in filesToUpload)
            {
                file.content?.Dispose();
            }
            
            _isUploading = false;
            _uploadProgress = 0;
            await InvokeAsync(StateHasChanged);
        }
    }
    
    /// <summary>Updates progress and estimates time remaining.</summary>
    private void UpdateProgress(int progress, string status)
    {
        _uploadProgress = progress;
        _uploadStatus = status;
        
        if (progress > 0 && progress < 100)
        {
            TimeSpan elapsed = DateTime.UtcNow - _uploadStartTime;
            double estimatedTotal = elapsed.TotalSeconds / (progress / 100.0);
            double remaining = estimatedTotal - elapsed.TotalSeconds;
            
            if (remaining > 60)
            {
                _estimatedTimeRemaining = $"~{Math.Ceiling(remaining / 60)} min remaining";
            }
            else if (remaining > 0)
            {
                _estimatedTimeRemaining = $"~{Math.Ceiling(remaining)} sec remaining";
            }
            else
            {
                _estimatedTimeRemaining = "";
            }
        }
        else
        {
            _estimatedTimeRemaining = "";
        }
        
        InvokeAsync(StateHasChanged);
    }
    
    /// <summary>Clears selected files and resets the uploader.</summary>
    public void ClearSelection()
    {
        _selectedFiles.Clear();
        _detectedCollection = null;
        _errorMessage = null;
        ResetFileInput();
        StateHasChanged();
    }

    /// <summary>Discovers available configs/splits for a HuggingFace dataset.</summary>
    public async Task DiscoverHuggingFaceDatasetAsync()
    {
        if (string.IsNullOrWhiteSpace(_hfRepository))
        {
            _errorMessage = "Please enter a HuggingFace repository name.";
            return;
        }

        _errorMessage = null;
        _hfDiscovering = true;
        _hfShowOptions = false;
        _hfDiscoveryResponse = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            Logs.Info($"[HF DISCOVERY] Starting discovery for {_hfRepository}");

            _hfDiscoveryResponse = await DatasetApiClient.DiscoverHuggingFaceDatasetAsync(
                new HuggingFaceDiscoveryRequest
                {
                    Repository = _hfRepository,
                    Revision = _hfRevision,
                    IsStreaming = _hfIsStreaming,
                    AccessToken = _hfAccessToken
                });

            if (_hfDiscoveryResponse != null && _hfDiscoveryResponse.IsAccessible)
            {
                // Respect user's choice of streaming vs download mode
                Logs.Info($"[HF DISCOVERY] User selected streaming mode: {_hfIsStreaming}");
                
                // Check if we need to show options or can auto-import
                bool needsUserSelection = false;

                if (_hfIsStreaming && _hfDiscoveryResponse.StreamingOptions != null)
                {
                    // Show options if multiple configs/splits available
                    needsUserSelection = _hfDiscoveryResponse.StreamingOptions.AvailableOptions.Count > 1;
                }
                else if (!_hfIsStreaming && _hfDiscoveryResponse.DownloadOptions != null)
                {
                    // Show options if multiple files available
                    needsUserSelection = _hfDiscoveryResponse.DownloadOptions.AvailableFiles.Count > 1;
                }

                if (needsUserSelection)
                {
                    _hfShowOptions = true;
                    Logs.Info($"[HF DISCOVERY] Multiple options found, showing selection UI");
                }
                else
                {
                    // Auto-import with single option
                    Logs.Info($"[HF DISCOVERY] Single option found, auto-importing");
                    await ImportFromHuggingFaceAsync(null, null, null);
                }
            }
            else
            {
                _errorMessage = _hfDiscoveryResponse?.ErrorMessage ?? "Failed to discover dataset options.";
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"[HF DISCOVERY] Discovery failed: {ex.Message}");
            _errorMessage = $"Discovery failed: {ex.Message}";
        }
        finally
        {
            _hfDiscovering = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Cancels the dataset options selection.</summary>
    public void CancelHuggingFaceOptions()
    {
        _hfShowOptions = false;
        _hfDiscoveryResponse = null;
        StateHasChanged();
    }

    /// <summary>Confirms dataset options and starts import.</summary>
    public async Task ConfirmHuggingFaceOptions(string? config, string? split, string? dataFilePath)
    {
        _hfShowOptions = false;
        await ImportFromHuggingFaceAsync(config, split, dataFilePath);
    }

    /// <summary>Imports a dataset from HuggingFace Hub.</summary>
    public async Task ImportFromHuggingFaceAsync(string? selectedConfig = null, string? selectedSplit = null, string? selectedDataFile = null, bool confirmedDownloadFallback = false)
    {
        if (string.IsNullOrWhiteSpace(_hfRepository))
        {
            _errorMessage = "Please enter a HuggingFace repository name.";
            return;
        }

        _errorMessage = null;
        _isUploading = true;
        _uploadStatus = "Creating dataset...";
        await InvokeAsync(StateHasChanged);

        try
        {
            // Step 1: Create dataset
            string datasetName = !string.IsNullOrWhiteSpace(_hfDatasetName)
                ? _hfDatasetName
                : _hfRepository.Split('/').Last();

            string description = !string.IsNullOrWhiteSpace(_hfDescription)
                ? _hfDescription
                : $"Imported from HuggingFace: {_hfRepository}";

            DatasetDetailDto? dataset = await DatasetApiClient.CreateDatasetAsync(
                new CreateDatasetRequest(datasetName, description));

            if (dataset == null)
            {
                throw new Exception("Failed to create dataset on server.");
            }

            Guid datasetId = dataset.Id;
            Logs.Info($"Dataset created with ID: {datasetId} for HuggingFace import");

            // Step 2: Trigger HuggingFace import
            _uploadStatus = _hfIsStreaming
                ? "Creating streaming reference..."
                : "Downloading from HuggingFace...";
            await InvokeAsync(StateHasChanged);

            bool success = await DatasetApiClient.ImportFromHuggingFaceAsync(
                datasetId,
                new ImportHuggingFaceDatasetRequest
                {
                    Repository = _hfRepository,
                    Revision = _hfRevision,
                    Name = datasetName,
                    Description = description,
                    IsStreaming = _hfIsStreaming && !confirmedDownloadFallback,
                    AccessToken = _hfAccessToken,
                    Config = selectedConfig,
                    Split = selectedSplit,
                    DataFilePath = selectedDataFile,
                    ConfirmedDownloadFallback = confirmedDownloadFallback
                });

            if (!success)
            {
                throw new Exception("HuggingFace import request failed.");
            }

            _uploadStatus = _hfIsStreaming
                ? "Streaming reference created!"
                : "Import started. Processing in background...";

            await InvokeAsync(StateHasChanged);

            // Step 3: Handle completion differently for streaming vs download mode
            if (_hfIsStreaming)
            {
                // Streaming mode: dataset is a lightweight reference; items are streamed on demand
                Logs.Info($"Streaming reference created for dataset {datasetId}. Preparing viewer...");

                // Give the server a brief moment to finalize streaming metadata
                await Task.Delay(2000);

                DatasetDetailDto? updatedDataset = await DatasetApiClient.GetDatasetAsync(datasetId);
                if (updatedDataset != null)
                {
                    Logs.Info($"Streaming dataset {datasetId} status: {updatedDataset.Status}, TotalItems: {updatedDataset.TotalItems}");
                    
                    // Check if streaming failed and offer fallback
                    if (updatedDataset.Status == IngestionStatusDto.Failed && 
                        updatedDataset.ErrorMessage?.StartsWith("STREAMING_UNAVAILABLE:") == true)
                    {
                        string reason = updatedDataset.ErrorMessage.Substring("STREAMING_UNAVAILABLE:".Length);
                        Logs.Warning($"[HF IMPORT] Streaming failed: {reason}");
                        
                        // Ask user if they want to fallback to download mode
                        bool? result = await DialogService.ShowMessageBox(
                            "Streaming Not Available",
                            $"Streaming mode is not supported for this dataset.\n\nReason: {reason}\n\nWould you like to download the dataset instead? This may require significant disk space and time.",
                            yesText: "Download Dataset",
                            cancelText: "Cancel");
                        
                        if (result == true)
                        {
                            Logs.Info("[HF IMPORT] User confirmed download fallback, restarting import...");
                            
                            // Delete the failed dataset
                            await DatasetApiClient.DeleteDatasetAsync(datasetId);
                            
                            // Retry with download fallback flag
                            await ImportFromHuggingFaceAsync(selectedConfig, selectedSplit, selectedDataFile, confirmedDownloadFallback: true);
                            return;
                        }
                        else
                        {
                            Logs.Info("[HF IMPORT] User declined download fallback");
                            
                            // Delete the failed dataset
                            await DatasetApiClient.DeleteDatasetAsync(datasetId);
                            
                            NotificationService.ShowWarning("Import cancelled. Streaming is not available for this dataset.");
                            
                            _hfRepository = string.Empty;
                            _hfDatasetName = null;
                            _hfDescription = null;
                            _hfRevision = null;
                            _hfAccessToken = null;
                            
                            return;
                        }
                    }
                }

                try
                {
                    DatasetState.SetLoading(true);
                    await DatasetCacheService.LoadFirstPageAsync(datasetId);
                    DatasetState.SetLoading(false);

                    NotificationService.ShowSuccess(
                        $"Streaming dataset '{datasetName}' imported successfully. Images will be streamed directly from HuggingFace.");
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to load streaming dataset {datasetId} into viewer: {ex.Message}");
                    NotificationService.ShowError($"Streaming dataset was created, but loading items failed: {ex.Message}");
                }

                // Clear form
                _hfRepository = string.Empty;
                _hfDatasetName = null;
                _hfDescription = null;
                _hfRevision = null;
                _hfAccessToken = null;

                await Task.Delay(1000);
                NavigationService.NavigateToDataset(datasetId.ToString());
            }
            else
            {
                // Download mode: Wait for processing and then try to load
                _uploadStatus = "Waiting for processing to complete...";
                await InvokeAsync(StateHasChanged);

                Logs.Info($"Download mode import started for dataset {datasetId}. Waiting for background processing...");

                // Poll for completion (wait a bit longer for processing)
                await Task.Delay(5000);

                // Check dataset status
                DatasetDetailDto? updatedDataset = await DatasetApiClient.GetDatasetAsync(datasetId);
                if (updatedDataset != null)
                {
                    Logs.Info($"Dataset {datasetId} status: {updatedDataset.Status}, TotalItems: {updatedDataset.TotalItems}");

                    if (updatedDataset.Status == IngestionStatusDto.Completed && updatedDataset.TotalItems > 0)
                    {
                        // Success! Load the dataset
                        DatasetState.SetLoading(true);
                        await DatasetCacheService.LoadFirstPageAsync(datasetId);
                        DatasetState.SetLoading(false);

                        NotificationService.ShowSuccess($"Dataset '{datasetName}' imported successfully with {updatedDataset.TotalItems} items!");

                        // Clear form
                        _hfRepository = string.Empty;
                        _hfDatasetName = null;
                        _hfDescription = null;
                        _hfRevision = null;
                        _hfAccessToken = null;

                        await Task.Delay(1000);
                        NavigationService.NavigateToDataset(datasetId.ToString());
                    }
                    else if (updatedDataset.Status == IngestionStatusDto.Failed)
                    {
                        string errorDetail = !string.IsNullOrWhiteSpace(updatedDataset.ErrorMessage) 
                            ? $" Error: {updatedDataset.ErrorMessage}" 
                            : "";
                        throw new Exception($"Dataset import failed. Status: {updatedDataset.Status}.{errorDetail}");
                    }
                    else
                    {
                        // Still processing
                        NotificationService.ShowInfo(
                            $"Dataset '{datasetName}' import started. Processing in background... " +
                            $"Current status: {updatedDataset.Status}. Check the dashboard in a moment.");

                        // Clear form
                        _hfRepository = string.Empty;
                        _hfDatasetName = null;
                        _hfDescription = null;
                        _hfRevision = null;
                        _hfAccessToken = null;
                    }
                }
                else
                {
                    Logs.Warning($"Could not fetch updated dataset status for {datasetId}");
                    NotificationService.ShowInfo($"Dataset '{datasetName}' import started. Check the dashboard in a moment.");

                    // Clear form anyway
                    _hfRepository = string.Empty;
                    _hfDatasetName = null;
                    _hfDescription = null;
                    _hfRevision = null;
                    _hfAccessToken = null;
                }
            }
        }
        catch (Exception ex)
        {
            string userMessage = GetFriendlyErrorMessage(ex);
            _errorMessage = userMessage;
            Logs.Error("Failed to import from HuggingFace", ex);
            DatasetState.SetError(userMessage);
            NotificationService.ShowError(userMessage);
        }
        finally
        {
            _isUploading = false;
            _uploadStatus = string.Empty;
            await InvokeAsync(StateHasChanged);
        }
    }

    // TODO: Add file validation (check headers, sample data)
    // TODO: Add resumable upload for very large files
    // TODO: Add ZIP extraction using System.IO.Compression
    // TODO: Add multi-part CSV000 file handling
    // TODO: Add preview of first few rows before full parse
    // TODO: Add drag-drop file access via JavaScript interop
}
