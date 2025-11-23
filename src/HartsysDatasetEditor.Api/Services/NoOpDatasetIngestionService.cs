using System.Text;
using System.IO.Compression;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// Placeholder ingestion service. Updates dataset status without processing.
/// TODO: Replace with real ingestion pipeline (see docs/architecture.md section 3.3).
/// </summary>
internal sealed class NoOpDatasetIngestionService(IDatasetRepository datasetRepository, IDatasetItemRepository datasetItemRepository,
    IHuggingFaceClient huggingFaceClient) : IDatasetIngestionService
{
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
            HuggingFaceDatasetInfo? info = await huggingFaceClient.GetDatasetInfoAsync(request.Repository, request.Revision, request.AccessToken, cancellationToken);

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
            if (request.IsStreaming)
            {
                Logs.Info("[HF IMPORT] Step 3: Configuring STREAMING mode");
                Logs.Warning("[HF IMPORT] WARNING: Streaming mode is experimental - dataset will show 0 items");

                dataset.Status = IngestionStatusDto.Completed;
                dataset.TotalItems = 0; // Items will be fetched on-demand (not yet implemented)
                await datasetRepository.UpdateAsync(dataset, cancellationToken);

                Logs.Info($"[HF IMPORT] Dataset {datasetId} configured as streaming reference");
                Logs.Info($"[HF IMPORT] Final status: {dataset.Status}, TotalItems: {dataset.TotalItems}");
                Logs.Info("========== [HF IMPORT COMPLETE - STREAMING] ==========");
            }
            else
            {
                Logs.Info("[HF IMPORT] Step 3: Starting DOWNLOAD mode");

                // Download mode: Find and download dataset files
                List<HuggingFaceDatasetFile> dataFiles = info.Files
                    .Where(f => f.Type == "csv" || f.Type == "json" || f.Type == "parquet")
                    .ToList();

                Logs.Info($"[HF IMPORT] Found {dataFiles.Count} supported data files (csv/json/parquet)");

                if (dataFiles.Count == 0)
                {
                    Logs.Error($"[HF IMPORT] FAIL: No supported data files found in {request.Repository}");
                    Logs.Error($"[HF IMPORT] Available files: {string.Join(", ", info.Files.Select(f => f.Path))}");
                    dataset.Status = IngestionStatusDto.Failed;
                    await datasetRepository.UpdateAsync(dataset, cancellationToken);
                    return;
                }

                // For now, download the first supported file
                HuggingFaceDatasetFile fileToDownload = dataFiles[0];
                Logs.Info($"[HF IMPORT] Downloading file: {fileToDownload.Path} ({fileToDownload.Type}, {fileToDownload.Size} bytes)");

                string tempDownloadPath = Path.Combine(
                    Path.GetTempPath(),
                    $"hf-dataset-{datasetId}-{Path.GetFileName(fileToDownload.Path)}");

                Logs.Info($"[HF IMPORT] Download destination: {tempDownloadPath}");

                await huggingFaceClient.DownloadFileAsync(request.Repository, fileToDownload.Path, tempDownloadPath, request.Revision, request.AccessToken, cancellationToken);

                Logs.Info($"[HF IMPORT] Download complete. File size: {new FileInfo(tempDownloadPath).Length} bytes");

                await datasetRepository.UpdateAsync(dataset, cancellationToken);

                // Process the downloaded file
                Logs.Info("[HF IMPORT] Starting ingestion pipeline...");
                await StartIngestionAsync(datasetId, tempDownloadPath, cancellationToken);
                Logs.Info("========== [HF IMPORT COMPLETE - DOWNLOAD] ==========");
            }
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

            string fileToProcess = uploadLocation;
            string? tempExtractedPath = null;
            Dictionary<string, Dictionary<string, string>>? auxiliaryMetadata = null;
            
            // Check if uploaded file is a ZIP
            if (Path.GetExtension(uploadLocation).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Logs.Info($"Extracting ZIP file for dataset {datasetId}");
                
                // Create temp directory for extraction
                tempExtractedPath = Path.Combine(Path.GetTempPath(), $"dataset-{datasetId}-extracted-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempExtractedPath);
                
                // Extract ZIP to temp directory
                ZipFile.ExtractToDirectory(uploadLocation, tempExtractedPath);
                
                // Find the primary dataset file (photos.tsv000 or photos.csv000)
                string[] extractedFiles = Directory.GetFiles(tempExtractedPath, "*.*", SearchOption.AllDirectories);
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
                
                fileToProcess = primaryFile;
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
                }
                else
                {
                    Logs.Info($"Found primary file in ZIP: {Path.GetFileName(primaryFile)}");
                }
            }

            List<DatasetItemDto> parsedItems;
            string extension = Path.GetExtension(fileToProcess);
            if (extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase))
            {
                parsedItems = await ParseParquetAsync(datasetId, fileToProcess, cancellationToken);
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
        string externalId = GetFirstNonEmptyString(values, "id", "image_id", "uid", "uuid") ?? string.Empty;
        string? title = GetFirstNonEmptyString(values, "title", "caption", "text", "description", "label");
        string? description = GetFirstNonEmptyString(values, "description", "caption", "text");
        string? imageUrl = GetFirstNonEmptyString(values, "image_url", "img_url", "url");
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

    public void TryDeleteTempFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logs.Debug($"Failed to delete temp file {path}: {ex.GetType().Name}: {ex.Message}");
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
