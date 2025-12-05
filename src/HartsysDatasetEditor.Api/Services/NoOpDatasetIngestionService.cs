using System.Text;
using System.Text.Json;
using System.IO.Compression;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic.FileIO;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// Placeholder ingestion service. Updates dataset status and parses supported formats.
/// TODO: Replace with real ingestion pipeline (see docs/architecture.md section 3.3).
/// </summary>
internal sealed class NoOpDatasetIngestionService(
    IDatasetRepository datasetRepository,
    IDatasetItemRepository datasetItemRepository,
    IHuggingFaceClient huggingFaceClient,
    IHuggingFaceDatasetServerClient huggingFaceDatasetServerClient,
    IConfiguration configuration) : IDatasetIngestionService
{
    private readonly string _datasetRootPath = configuration["Storage:DatasetRootPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "datasets");
    private readonly string _uploadRootPath = configuration["Storage:UploadPath"] ?? "./uploads";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default)
    {
        Logs.Info("========== [HF IMPORT START] ==========");
        Logs.Info($"[HF IMPORT] Dataset ID: {datasetId}");
        Logs.Info($"[HF IMPORT] Repository: {request.Repository}");
        Logs.Info($"[HF IMPORT] Streaming: {request.IsStreaming}");
        Logs.Info($"[HF IMPORT] Revision: {request.Revision ?? "main"}");

        DatasetEntity? dataset = await datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            Logs.Error($"[HF IMPORT] FATAL: Dataset {datasetId} not found in repository");
            return;
        }

        Logs.Info($"[HF IMPORT] Dataset found. Current status: {dataset.Status}");

        try
        {
            dataset.Status = IngestionStatusDto.Processing;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);
            Logs.Info("[HF IMPORT] Status updated to Processing");

            // Step 1: Validate dataset exists and fetch metadata
            Logs.Info("[HF IMPORT] Step 1: Fetching metadata from HuggingFace Hub...");
            HuggingFaceDatasetInfo? info = await huggingFaceClient.GetDatasetInfoAsync(
                request.Repository,
                request.Revision,
                request.AccessToken,
                cancellationToken);

            if (info == null)
            {
                Logs.Error($"[HF IMPORT] FAIL: Dataset {request.Repository} not found or inaccessible on HuggingFace Hub");
                dataset.Status = IngestionStatusDto.Failed;
                await datasetRepository.UpdateAsync(dataset, cancellationToken);
                return;
            }

            Logs.Info($"[HF IMPORT] SUCCESS: Found dataset {request.Repository}");
            Logs.Info($"[HF IMPORT] File count: {info.Files.Count}");
            Logs.Info($"[HF IMPORT] Files: {string.Join(", ", info.Files.Select(f => $"{f.Path} ({f.Type}, {f.Size} bytes)"))}");

            HuggingFaceDatasetProfile profile = HuggingFaceDatasetProfile.FromDatasetInfo(request.Repository, info);

            // Step 2: Update dataset metadata
            Logs.Info("[HF IMPORT] Step 2: Updating dataset metadata...");
            string sourceUri = $"https://huggingface.co/datasets/{request.Repository}";
            if (!string.IsNullOrWhiteSpace(request.Revision))
            {
                sourceUri += $"/tree/{request.Revision}";
            }

            dataset.SourceType = request.IsStreaming
                ? DatasetSourceType.HuggingFaceStreaming
                : DatasetSourceType.HuggingFaceDownload;
            dataset.SourceUri = sourceUri;
            dataset.IsStreaming = request.IsStreaming;

            Logs.Info($"[HF IMPORT] SourceType: {dataset.SourceType}");
            Logs.Info($"[HF IMPORT] SourceUri: {dataset.SourceUri}");

            // Step 3: Handle streaming vs download mode
            bool streamingRequested = request.IsStreaming;

            if (streamingRequested)
            {
                Logs.Info("[HF IMPORT] Step 3: Attempting streaming configuration via datasets-server");

                dataset.HuggingFaceRepository = request.Repository;
                string? accessToken = request.AccessToken;

                // Check if user explicitly provided config/split (from discovery UI)
                bool userProvidedConfig = !string.IsNullOrWhiteSpace(request.Config) || !string.IsNullOrWhiteSpace(request.Split);

                if (userProvidedConfig)
                {
                    // User selected a specific config/split - use it directly
                    Logs.Info($"[HF IMPORT] Using user-selected config/split: config={request.Config ?? "default"}, split={request.Split ?? "train"}");
                    
                    dataset.HuggingFaceConfig = request.Config;
                    dataset.HuggingFaceSplit = request.Split ?? "train";

                    // Try to get row count for this specific config/split
                    HuggingFaceDatasetSizeInfo? sizeInfo = await huggingFaceDatasetServerClient.GetDatasetSizeAsync(
                        request.Repository,
                        request.Config,
                        request.Split,
                        accessToken,
                        cancellationToken);

                    if (sizeInfo?.NumRows.HasValue == true)
                    {
                        dataset.TotalItems = sizeInfo.NumRows.Value;
                    }

                    dataset.SourceType = DatasetSourceType.HuggingFaceStreaming;
                    dataset.IsStreaming = true;
                    dataset.Status = IngestionStatusDto.Completed;
                    await datasetRepository.UpdateAsync(dataset, cancellationToken);

                    Logs.Info($"[HF IMPORT] Dataset {datasetId} configured as streaming reference (user-selected)");
                    Logs.Info($"[HF IMPORT] Streaming config: repo={dataset.HuggingFaceRepository}, config={dataset.HuggingFaceConfig}, split={dataset.HuggingFaceSplit}, totalRows={dataset.TotalItems}");
                    Logs.Info("========== [HF IMPORT COMPLETE - STREAMING] ==========");
                    return;
                }

                // No user-provided config/split - use auto-discovery
                HuggingFaceStreamingPlan streamingPlan = await HuggingFaceStreamingStrategy.DiscoverStreamingPlanAsync(
                    huggingFaceDatasetServerClient,
                    request.Repository,
                    accessToken,
                    cancellationToken);

                if (streamingPlan.IsStreamingSupported)
                {
                    dataset.HuggingFaceConfig = streamingPlan.Config;

                    string? inferredSplit = streamingPlan.Split;
                    if (string.IsNullOrWhiteSpace(inferredSplit))
                    {
                        inferredSplit = "train";
                    }

                    dataset.HuggingFaceSplit = inferredSplit;

                    if (streamingPlan.TotalRows.HasValue)
                    {
                        dataset.TotalItems = streamingPlan.TotalRows.Value;
                    }

                    dataset.SourceType = DatasetSourceType.HuggingFaceStreaming;
                    dataset.IsStreaming = true;
                    dataset.Status = IngestionStatusDto.Completed;
                    await datasetRepository.UpdateAsync(dataset, cancellationToken);

                    Logs.Info($"[HF IMPORT] Dataset {datasetId} configured as streaming reference (auto-discovered)");
                    Logs.Info($"[HF IMPORT] Streaming config: repo={dataset.HuggingFaceRepository}, config={dataset.HuggingFaceConfig}, split={dataset.HuggingFaceSplit}, totalRows={dataset.TotalItems}, source={streamingPlan.Source}");
                    Logs.Info("========== [HF IMPORT COMPLETE - STREAMING] ==========");
                    return;
                }

                // If we reach here, streaming was requested but could not be configured.
                // Do NOT automatically fall back - require user confirmation
                if (!request.ConfirmedDownloadFallback)
                {
                    string failureReason = streamingPlan.FailureReason ?? "Streaming not supported for this dataset";
                    Logs.Warning($"[HF IMPORT] Streaming mode requested but not supported for this dataset. Reason: {failureReason}");
                    Logs.Warning($"[HF IMPORT] Fallback to download mode requires user confirmation. Failing import.");
                    
                    // Mark as failed with special error code that client can detect
                    dataset.Status = IngestionStatusDto.Failed;
                    dataset.ErrorMessage = $"STREAMING_UNAVAILABLE:{failureReason}";
                    await datasetRepository.UpdateAsync(dataset, cancellationToken);
                    
                    Logs.Info("========== [HF IMPORT FAILED - STREAMING UNAVAILABLE] ==========");
                    return;
                }
                
                // User confirmed fallback to download mode
                Logs.Info($"[HF IMPORT] User confirmed fallback to download mode. Reason: {streamingPlan.FailureReason ?? "unknown"}");
                dataset.SourceType = DatasetSourceType.HuggingFaceDownload;
                dataset.IsStreaming = false;
            }

            // Download mode ingestion
            Logs.Info("[HF IMPORT] Step 3: Starting DOWNLOAD mode");

            List<HuggingFaceDatasetFile> dataFiles = profile.DataFiles.ToList();

            Logs.Info($"[HF IMPORT] Found {dataFiles.Count} supported data files (csv/json/parquet)");

            if (dataFiles.Count == 0)
            {
                Logs.Warning($"[HF IMPORT] No CSV/JSON/Parquet files found in {request.Repository}, attempting image-only import");
                Logs.Info($"[HF IMPORT] Available files: {string.Join(", ", info.Files.Select(f => f.Path))}");

                bool imageImportSucceeded = await TryImportImageOnlyDatasetFromHuggingFaceAsync(dataset, info, request, cancellationToken);
                if (!imageImportSucceeded)
                {
                    dataset.Status = IngestionStatusDto.Failed;
                    dataset.ErrorMessage = $"No supported data files (CSV/JSON/Parquet) or image files found in {request.Repository}. " +
                        $"Available files: {string.Join(", ", info.Files.Take(10).Select(f => f.Path))}" +
                        (info.Files.Count > 10 ? $" and {info.Files.Count - 10} more..." : "");
                    await datasetRepository.UpdateAsync(dataset, cancellationToken);
                }

                return;
            }

            HuggingFaceDatasetFile fileToDownload = dataFiles[0];
            Logs.Info($"[HF IMPORT] Downloading file: {fileToDownload.Path} ({fileToDownload.Type}, {fileToDownload.Size} bytes)");

            string tempDownloadPath = Path.Combine(
                Path.GetTempPath(),
                $"hf-dataset-{datasetId}-{Path.GetFileName(fileToDownload.Path)}");

            Logs.Info($"[HF IMPORT] Download destination: {tempDownloadPath}");

            await huggingFaceClient.DownloadFileAsync(
                request.Repository,
                fileToDownload.Path,
                tempDownloadPath,
                request.Revision,
                request.AccessToken,
                cancellationToken);

            Logs.Info($"[HF IMPORT] Download complete. File size: {new FileInfo(tempDownloadPath).Length} bytes");

            await datasetRepository.UpdateAsync(dataset, cancellationToken);

            // Process the downloaded file
            Logs.Info("[HF IMPORT] Starting ingestion pipeline...");
            await StartIngestionAsync(datasetId, tempDownloadPath, cancellationToken);
            Logs.Info("========== [HF IMPORT COMPLETE - DOWNLOAD] ==========");
        }
        catch (Exception ex)
        {
            Logs.Error($"[HF IMPORT] EXCEPTION: Failed to import dataset {request.Repository} for dataset {datasetId}", ex);
            Logs.Error($"[HF IMPORT] Exception type: {ex.GetType().Name}");
            Logs.Error($"[HF IMPORT] Exception message: {ex.Message}");
            dataset.Status = IngestionStatusDto.Failed;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);
            Logs.Info($"[HF IMPORT] Dataset {datasetId} status set to Failed");
            Logs.Info("========== [HF IMPORT FAILED] ==========");
        }
    }

    private async Task<bool> TryImportImageOnlyDatasetFromHuggingFaceAsync(
        DatasetEntity dataset,
        HuggingFaceDatasetInfo info,
        ImportHuggingFaceDatasetRequest request,
        CancellationToken cancellationToken)
    {
        List<HuggingFaceDatasetFile> imageFiles = info.Files
            .Where(f =>
            {
                string extension = Path.GetExtension(f.Path).ToLowerInvariant();
                return extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".webp" || extension == ".gif" || extension == ".bmp";
            })
            .ToList();

        Logs.Info($"[HF IMPORT] Image-only fallback: found {imageFiles.Count} image files");

        if (imageFiles.Count == 0)
        {
            Logs.Error($"[HF IMPORT] FAIL: No supported CSV/JSON/Parquet files or image files found in {request.Repository}");
            return false;
        }

        List<DatasetItemDto> items = new(imageFiles.Count);
        string revision = string.IsNullOrWhiteSpace(request.Revision) ? "main" : request.Revision!;

        foreach (HuggingFaceDatasetFile file in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string imagePath = file.Path;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                continue;
            }

            string imageUrl = $"https://huggingface.co/datasets/{request.Repository}/resolve/{revision}/{imagePath}";
            string externalId = Path.GetFileNameWithoutExtension(imagePath);
            string title = externalId;

            Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase)
            {
                ["hf_path"] = imagePath
            };

            DatasetItemDto item = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Title = title,
                Description = null,
                ImageUrl = imageUrl,
                ThumbnailUrl = imageUrl,
                Width = 0,
                Height = 0,
                Metadata = metadata
            };

            items.Add(item);
        }

        if (items.Count == 0)
        {
            Logs.Error($"[HF IMPORT] FAIL: No dataset items could be created from image files in {request.Repository}");
            return false;
        }

        await datasetItemRepository.AddRangeAsync(dataset.Id, items, cancellationToken);
        dataset.TotalItems = items.Count;
        dataset.Status = IngestionStatusDto.Completed;
        await datasetRepository.UpdateAsync(dataset, cancellationToken);
        Logs.Info($"[HF IMPORT] Image-only dataset imported with {items.Count} items");

        string dummyUpload = Path.Combine(Path.GetTempPath(), $"hf-images-{dataset.Id}.tmp");
        string datasetFolder = GetDatasetFolderPath(dataset, dummyUpload);
        await WriteDatasetMetadataFileAsync(dataset, datasetFolder, null, new List<string>(), cancellationToken);

        Logs.Info($"[HF IMPORT] Final status: {dataset.Status}, TotalItems: {dataset.TotalItems}");
        Logs.Info("========== [HF IMPORT COMPLETE - IMAGE-ONLY] ==========");

        return true;
    }

    public async Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default)
    {
        DatasetEntity? dataset = await datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            Logs.Warning($"Dataset {datasetId} not found during ingestion");
            return;
        }

        if (string.IsNullOrWhiteSpace(uploadLocation) || !File.Exists(uploadLocation))
        {
            Logs.Warning($"Upload location missing for dataset {datasetId}");
            dataset.Status = IngestionStatusDto.Failed;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);
            return;
        }

        try
        {
            dataset.Status = IngestionStatusDto.Processing;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);

            string datasetFolder = GetDatasetFolderPath(dataset, uploadLocation);

            string fileToProcess = uploadLocation;
            string? tempExtractedPath = null;
            Dictionary<string, Dictionary<string, string>>? auxiliaryMetadata = null;
            string? primaryFileForMetadata = null;
            List<string> auxiliaryFilesForMetadata = new();

            if (Path.GetExtension(uploadLocation).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Logs.Info($"Extracting ZIP file for dataset {datasetId}");

                tempExtractedPath = Path.Combine(Path.GetTempPath(), $"dataset-{datasetId}-extracted-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempExtractedPath);

                ZipFile.ExtractToDirectory(uploadLocation, tempExtractedPath);

                string[] extractedFiles = Directory.GetFiles(tempExtractedPath, "*.*", System.IO.SearchOption.AllDirectories);
                string? primaryFile = extractedFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).StartsWith("photos", StringComparison.OrdinalIgnoreCase) &&
                    (f.EndsWith(".tsv000", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".csv000", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));

                if (primaryFile == null)
                {
                    throw new InvalidOperationException("No primary dataset file (photos.tsv/csv) found in ZIP archive");
                }

                string primaryDestination = Path.Combine(datasetFolder, Path.GetFileName(primaryFile));
                File.Copy(primaryFile, primaryDestination, overwrite: true);
                fileToProcess = primaryDestination;
                primaryFileForMetadata = Path.GetFileName(primaryDestination);
                Logs.Info($"Found primary file in ZIP: {Path.GetFileName(primaryFile)}");

                string[] auxiliaryFiles = extractedFiles
                    .Where(f => !f.Equals(primaryFile, StringComparison.OrdinalIgnoreCase) &&
                                (f.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".tsv000", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".csv000", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (auxiliaryFiles.Length > 0)
                {
                    Logs.Info($"Found {auxiliaryFiles.Length} auxiliary metadata files: {string.Join(", ", auxiliaryFiles.Select(f => Path.GetRelativePath(tempExtractedPath, f)))}");
                    auxiliaryMetadata = await LoadAuxiliaryMetadataAsync(auxiliaryFiles, cancellationToken);

                    foreach (string auxiliaryFile in auxiliaryFiles)
                    {
                        string auxDestination = Path.Combine(datasetFolder, Path.GetFileName(auxiliaryFile));
                        File.Copy(auxiliaryFile, auxDestination, overwrite: true);
                        auxiliaryFilesForMetadata.Add(Path.GetFileName(auxDestination));
                    }
                }
                else
                {
                    Logs.Info($"Found primary file in ZIP: {Path.GetFileName(primaryFile)}");
                }
            }
            else
            {
                string destination = Path.Combine(datasetFolder, Path.GetFileName(uploadLocation));
                if (!string.Equals(uploadLocation, destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(uploadLocation, destination, overwrite: true);
                }

                fileToProcess = destination;
                primaryFileForMetadata = Path.GetFileName(destination);
            }

            List<DatasetItemDto> parsedItems;
            string extension = Path.GetExtension(fileToProcess);
            if (extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase))
            {
                parsedItems = await ParseParquetAsync(datasetId, fileToProcess, cancellationToken);
            }
            else if (dataset.SourceType == DatasetSourceType.HuggingFaceDownload)
            {
                if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    parsedItems = await ParseHuggingFaceJsonAsync(datasetId, fileToProcess, cancellationToken);
                }
                else
                {
                    parsedItems = await ParseHuggingFaceCsvAsync(datasetId, fileToProcess, cancellationToken);
                }
            }
            else
            {
                parsedItems = await ParseUnsplashTsvAsync(fileToProcess, auxiliaryMetadata, cancellationToken);
            }
            if (parsedItems.Count > 0)
            {
                await datasetItemRepository.AddRangeAsync(datasetId, parsedItems, cancellationToken);
            }

            dataset.TotalItems = parsedItems.Count;
            dataset.Status = IngestionStatusDto.Completed;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);
            Logs.Info($"Ingestion completed for dataset {datasetId} with {parsedItems.Count} items");

            await WriteDatasetMetadataFileAsync(dataset, datasetFolder, primaryFileForMetadata, auxiliaryFilesForMetadata, cancellationToken);
            
            // Cleanup extracted files
            if (tempExtractedPath != null && Directory.Exists(tempExtractedPath))
            {
                try
                {
                    Directory.Delete(tempExtractedPath, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    Logs.Warning($"Failed to cleanup temp extraction directory: {tempExtractedPath}. Exception: {cleanupEx.GetType().Name}: {cleanupEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to ingest dataset {datasetId}", ex);
            dataset.Status = IngestionStatusDto.Failed;
            await datasetRepository.UpdateAsync(dataset, cancellationToken);
        }
        finally
        {
            TryDeleteTempFile(uploadLocation);
        }
    }

    public async Task<List<DatasetItemDto>> ParseUnsplashTsvAsync(string filePath, Dictionary<string, Dictionary<string, string>>? auxiliaryMetadata,
        CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        Logs.Info($"ParseUnsplashTsvAsync: Read {lines.Length} total lines from {Path.GetFileName(filePath)}");
        if (lines.Length <= 1)
        {
            return [];
        }
        string[] headers = lines[0].Split('\t').Select(h => h.Trim()).ToArray();
        Dictionary<string, int> headerIndex = headers.Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        string GetValue(string[] values, string column)
        {
            return headerIndex.TryGetValue(column, out int idx) && idx < values.Length ? values[idx].Trim() : string.Empty;
        }
        List<DatasetItemDto> items = new(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            string[] values = line.Split('\t');
            if (values.Length != headers.Length)
            {
                Logs.Debug($"Skipping row {i + 1} due to column mismatch");
                continue;
            }
            string imageUrl = GetValue(values, "photo_image_url");
            
            // Fix malformed URLs: Unsplash CSV uses double underscores for protocol separator
            // Example: "https:__images.unsplash.com_photo-123_file.jpg"
            // Should become: "https://images.unsplash.com/photo-123/file.jpg"
            if (!string.IsNullOrWhiteSpace(imageUrl) && imageUrl.Contains("__"))
            {
                // Replace double underscores with slashes (for protocol and path separators)
                imageUrl = imageUrl.Replace("__", "/");
                
                // Also replace single underscores after the domain (path separators)
                // But preserve underscores in filenames and photo IDs
                if (imageUrl.StartsWith("http"))
                {
                    int domainEnd = imageUrl.IndexOf(".com") + 4;
                    if (domainEnd > 4 && domainEnd < imageUrl.Length)
                    {
                        string domain = imageUrl.Substring(0, domainEnd);
                        string path = imageUrl.Substring(domainEnd);
                        path = path.Replace("_", "/");
                        imageUrl = domain + path;
                    }
                }
            }

            Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase)
            {
                ["photographer_username"] = GetValue(values, "photographer_username"),
                ["photo_url"] = GetValue(values, "photo_url"),
                ["photo_location_name"] = GetValue(values, "photo_location_name"),
                ["photo_location_latitude"] = GetValue(values, "photo_location_latitude"),
                ["photo_location_longitude"] = GetValue(values, "photo_location_longitude")
            };

            string externalId = GetValue(values, "photo_id");
            if (!string.IsNullOrWhiteSpace(externalId) && auxiliaryMetadata != null &&
                auxiliaryMetadata.TryGetValue(externalId, out Dictionary<string, string>? extraMetadata))
            {
                foreach ((string key, string value) in extraMetadata)
                {
                    if (!metadata.ContainsKey(key))
                    {
                        metadata[key] = value;
                    }
                }
            }

            string title = GetValue(values, "photo_description");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Untitled photo";
            }

            string width = GetValue(values, "photo_width");
            string height = GetValue(values, "photo_height");

            DatasetItemDto dto = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Title = title,
                Description = GetValue(values, "photo_description"),
                ImageUrl = imageUrl,
                ThumbnailUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : $"{imageUrl}?w=400&q=80",
                Width = int.TryParse(width, out int widthValue) ? widthValue : 0,
                Height = int.TryParse(height, out int heightValue) ? heightValue : 0,
                Metadata = metadata
            };

            items.Add(dto);
        }

        Logs.Info($"ParseUnsplashTsvAsync: Successfully parsed {items.Count} items out of {lines.Length - 1} lines");
        return items;
    }

    public async Task<List<DatasetItemDto>> ParseHuggingFaceCsvAsync(Guid datasetId, string filePath, CancellationToken cancellationToken)
    {
        Logs.Info($"ParseHuggingFaceCsvAsync: Reading CSV file {Path.GetFileName(filePath)} for dataset {datasetId}");

        List<DatasetItemDto> items = new List<DatasetItemDto>();

        if (!File.Exists(filePath))
        {
            Logs.Warning($"ParseHuggingFaceCsvAsync: File not found: {filePath}");
            return items;
        }

        await Task.Yield();

        using TextFieldParser parser = new TextFieldParser(filePath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        if (parser.EndOfData)
        {
            return items;
        }

        string[]? headers = parser.ReadFields();
        if (headers == null || headers.Length == 0)
        {
            Logs.Warning("ParseHuggingFaceCsvAsync: CSV file has no header row");
            return items;
        }

        string[] trimmedHeaders = new string[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            trimmedHeaders[i] = headers[i].Trim();
        }

        int rowCount = 0;

        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[]? fields = parser.ReadFields();
            if (fields == null || fields.Length == 0)
            {
                continue;
            }

            Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            int maxIndex = trimmedHeaders.Length;
            for (int i = 0; i < maxIndex; i++)
            {
                string header = trimmedHeaders[i];
                string value = i < fields.Length && fields[i] != null ? fields[i]! : string.Empty;
                values[header] = value;
            }

            DatasetItemDto item = CreateDatasetItemFromParquetRow(values);
            items.Add(item);
            rowCount++;
        }

        Logs.Info($"ParseHuggingFaceCsvAsync: Parsed {rowCount} items from {Path.GetFileName(filePath)}");
        return items;
    }

    public async Task<List<DatasetItemDto>> ParseHuggingFaceJsonAsync(Guid datasetId, string filePath, CancellationToken cancellationToken)
    {
        Logs.Info($"ParseHuggingFaceJsonAsync: Reading JSON file {Path.GetFileName(filePath)} for dataset {datasetId}");

        List<DatasetItemDto> items = new List<DatasetItemDto>();

        if (!File.Exists(filePath))
        {
            Logs.Warning($"ParseHuggingFaceJsonAsync: File not found: {filePath}");
            return items;
        }

        await using FileStream stream = File.OpenRead(filePath);
        JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in root.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Dictionary<string, object?> values = CreateDictionaryFromJsonElement(element);
                DatasetItemDto item = CreateDatasetItemFromParquetRow(values);
                items.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in dataElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    Dictionary<string, object?> values = CreateDictionaryFromJsonElement(element);
                    DatasetItemDto item = CreateDatasetItemFromParquetRow(values);
                    items.Add(item);
                }
            }
            else
            {
                Dictionary<string, object?> values = CreateDictionaryFromJsonElement(root);
                DatasetItemDto item = CreateDatasetItemFromParquetRow(values);
                items.Add(item);
            }
        }

        Logs.Info($"ParseHuggingFaceJsonAsync: Parsed {items.Count} items from {Path.GetFileName(filePath)}");
        return items;
    }

    public async Task<List<DatasetItemDto>> ParseParquetAsync(Guid datasetId, string filePath, CancellationToken cancellationToken)
    {
        Logs.Info($"ParseParquetAsync: Reading Parquet file {Path.GetFileName(filePath)} for dataset {datasetId}");
        List<DatasetItemDto> items = [];
        await using FileStream fileStream = File.OpenRead(filePath);
        using ParquetReader parquetReader = await ParquetReader.CreateAsync(fileStream);
        DataField[] dataFields = parquetReader.Schema.GetDataFields();
        for (int rowGroup = 0; rowGroup < parquetReader.RowGroupCount; rowGroup++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ParquetRowGroupReader groupReader = parquetReader.OpenRowGroupReader(rowGroup);
            DataColumn[] columns = new DataColumn[dataFields.Length];
            for (int c = 0; c < dataFields.Length; c++)
            {
                columns[c] = await groupReader.ReadColumnAsync(dataFields[c]);
            }
            int rowCount = columns.Length > 0 ? columns[0].Data.Length : 0;
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < columns.Length; c++)
                {
                    string columnName = columns[c].Field.Name;
                    Array dataArray = columns[c].Data;
                    object? value = dataArray.GetValue(rowIndex);
                    values[columnName] = value;
                }
                DatasetItemDto item = CreateDatasetItemFromParquetRow(values);
                items.Add(item);
            }
        }
        Logs.Info($"ParseParquetAsync: Parsed {items.Count} items from {Path.GetFileName(filePath)}");
        return items;
    }

    public DatasetItemDto CreateDatasetItemFromParquetRow(Dictionary<string, object?> values)
    {
        string externalId = GetFirstNonEmptyString(values, "id", "image_id", "uid", "uuid", "__key", "sample_id") ?? string.Empty;
        string? title = GetFirstNonEmptyString(values, "title", "caption", "text", "description", "label", "name");
        string? description = GetFirstNonEmptyString(values, "description", "caption", "text");
        string? imageUrl = GetFirstNonEmptyString(values, "image_url", "img_url", "url");
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            foreach ((string key, object? rawValue) in values)
            {
                if (rawValue == null)
                {
                    continue;
                }

                string candidate = rawValue.ToString() ?? string.Empty;
                if (IsLikelyImageUrl(candidate))
                {
                    imageUrl = candidate;
                    break;
                }
            }
        }
        int width = GetIntValue(values, "width", "image_width", "w");
        int height = GetIntValue(values, "height", "image_height", "h");
        List<string> tags = new();
        string? tagsValue = GetFirstNonEmptyString(values, "tags", "labels");
        if (!string.IsNullOrWhiteSpace(tagsValue))
        {
            string[] parts = tagsValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    tags.Add(trimmed);
                }
            }
        }
        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in values)
        {
            if (value == null)
            {
                continue;
            }
            string stringValue = value.ToString() ?? string.Empty;
            metadata[key] = stringValue;
        }
        DateTime now = DateTime.UtcNow;
        return new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Title = string.IsNullOrWhiteSpace(title) ? externalId : title,
            Description = description,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            ThumbnailUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static string? GetFirstNonEmptyString(
        IReadOnlyDictionary<string, object?> values,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out object? value) && value != null)
            {
                string stringValue = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    return stringValue;
                }
            }
        }
        return null;
    }

    public static int GetIntValue(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out object? value) && value != null)
            {
                if (value is int intValue)
                {
                    return intValue;
                }

                if (int.TryParse(value.ToString(), out int parsed))
                {
                    return parsed;
                }
            }
        }
        return 0;
    }

    private static Dictionary<string, object?> CreateDictionaryFromJsonElement(JsonElement element)
    {
        Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            object? value = ConvertJsonElementToObject(property.Value);
            values[property.Name] = value;
        }

        return values;
    }

    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long longValue))
                {
                    return longValue;
                }

                if (element.TryGetDouble(out double doubleValue))
                {
                    return doubleValue;
                }

                return element.ToString();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return element.ToString();
        }
    }

    private static bool IsLikelyImageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string lower = value.ToLowerInvariant();
        if (!lower.Contains("http"))
        {
            return false;
        }

        return lower.EndsWith(".jpg", StringComparison.Ordinal) ||
               lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
               lower.EndsWith(".png", StringComparison.Ordinal) ||
               lower.EndsWith(".webp", StringComparison.Ordinal) ||
               lower.EndsWith(".gif", StringComparison.Ordinal) ||
               lower.EndsWith(".bmp", StringComparison.Ordinal);
    }

    public void TryDeleteTempFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);

            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            string uploadRoot = Path.GetFullPath(_uploadRootPath);
            string datasetRoot = Path.GetFullPath(_datasetRootPath);

            bool IsUnder(string root) => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(fullPath))
            {
                return;
            }

            if ((IsUnder(tempRoot) || IsUnder(uploadRoot)) && !IsUnder(datasetRoot))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            Logs.Debug($"Failed to delete temp file {path}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string GetDatasetFolderPath(DatasetEntity dataset, string uploadLocation)
    {
        string root = Path.GetFullPath(_datasetRootPath);
        Directory.CreateDirectory(root);

        string uploadFullPath = Path.GetFullPath(uploadLocation);
        string? uploadDirectory = Path.GetDirectoryName(uploadFullPath);

        if (!string.IsNullOrEmpty(uploadDirectory))
        {
            // If the upload already lives inside a subfolder of the dataset root, reuse that folder
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedUploadDir = uploadDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalizedUploadDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedUploadDir, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return uploadDirectory;
            }
        }

        // Otherwise, create a new slug-based folder for this dataset
        string slug = Slugify(dataset.Name);
        string shortId = dataset.Id.ToString("N")[..8];
        string folderName = $"{slug}-{shortId}";
        string datasetFolder = Path.Combine(root, folderName);
        Directory.CreateDirectory(datasetFolder);
        return datasetFolder;
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "dataset";
        }

        value = value.Trim().ToLowerInvariant();
        StringBuilder sb = new(value.Length);
        bool previousDash = false;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                previousDash = false;
            }
            else if (c == ' ' || c == '-' || c == '_' || c == '.')
            {
                if (!previousDash && sb.Length > 0)
                {
                    sb.Append('-');
                    previousDash = true;
                }
            }
        }

        if (sb.Length == 0)
        {
            return "dataset";
        }

        if (sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    private static async Task WriteDatasetMetadataFileAsync(
        DatasetEntity dataset,
        string datasetFolder,
        string? primaryFile,
        List<string> auxiliaryFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            DatasetDiskMetadata metadata = new()
            {
                Id = dataset.Id,
                Name = dataset.Name,
                Description = dataset.Description,
                SourceType = dataset.SourceType,
                SourceUri = dataset.SourceUri,
                SourceFileName = dataset.SourceFileName,
                PrimaryFile = primaryFile,
                AuxiliaryFiles = auxiliaryFiles
            };

            string metadataPath = Path.Combine(datasetFolder, "dataset.json");
            string json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Failed to write dataset metadata file for {dataset.Id}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> LoadAuxiliaryMetadataAsync(IEnumerable<string> files, CancellationToken cancellationToken)
    {
        Dictionary<string, Dictionary<string, string>> aggregate = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(file, cancellationToken);
                if (lines.Length <= 1)
                {
                    continue;
                }
                char separator = file.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tsv000", StringComparison.OrdinalIgnoreCase)
                    ? '\t' : ',';
                string[] headers = lines[0].Split(separator).Select(h => h.Trim()).ToArray();
                Logs.Info($"Parsing metadata file {Path.GetFileName(file)} with columns: {string.Join(", ", headers)}");
                int idIndex = Array.FindIndex(headers, h => h.Equals("photo_id", StringComparison.OrdinalIgnoreCase) ||
                                                        h.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                                                        h.Equals("image_id", StringComparison.OrdinalIgnoreCase));
                if (idIndex < 0)
                {
                    idIndex = 0;
                }
                int fileEntryCount = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    string[] values = line.Split(separator);
                    if (values.Length <= idIndex)
                    {
                        continue;
                    }
                    string photoId = values[idIndex].Trim();
                    if (string.IsNullOrWhiteSpace(photoId))
                    {
                        continue;
                    }
                    if (!aggregate.TryGetValue(photoId, out Dictionary<string, string>? target))
                    {
                        target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        aggregate[photoId] = target;
                    }
                    fileEntryCount++;
                    for (int h = 0; h < headers.Length && h < values.Length; h++)
                    {
                        if (h == idIndex)
                        {
                            continue;
                        }
                        string key = headers[h];
                        string value = values[h].Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !target.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
                        {
                            target[key] = value;
                        }
                    }
                }
                Logs.Info($"Loaded {fileEntryCount} rows from {Path.GetFileName(file)} (running distinct photo IDs: {aggregate.Count})");
            }
            catch (Exception ex)
            {
                Logs.Warning($"Failed to parse auxiliary metadata file {file}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return aggregate;
    }
}
