using System.Text;
using System.IO.Compression;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// Placeholder ingestion service. Updates dataset status without processing.
/// TODO: Replace with real ingestion pipeline (see docs/architecture.md section 3.3).
/// </summary>
internal sealed class NoOpDatasetIngestionService : IDatasetIngestionService
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetItemRepository _datasetItemRepository;
    private readonly ILogger<NoOpDatasetIngestionService> _logger;

    public NoOpDatasetIngestionService(
        IDatasetRepository datasetRepository,
        IDatasetItemRepository datasetItemRepository,
        ILogger<NoOpDatasetIngestionService> logger)
    {
        _datasetRepository = datasetRepository;
        _datasetItemRepository = datasetItemRepository;
        _logger = logger;
    }

    public Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[HF IMPORT] Dataset {DatasetId} requested repo {Repo} (streaming={Streaming})", datasetId, request.Repository, request.IsStreaming);

        // TODO: Implement Hugging Face downloader and ingestion pipeline.
        // 1. Validate dataset exists and update SourceType/SourceUri/IsStreaming fields.
        // 2. If IsStreaming == true, skip ingestion and mark dataset as read-only streaming reference.
        // 3. If IsStreaming == false, queue download job, persist dataset files, and call StartIngestionAsync.

        return Task.CompletedTask;
    }

    public async Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default)
    {
        DatasetEntity? dataset = await _datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            _logger.LogWarning("Dataset {DatasetId} not found during ingestion", datasetId);
            return;
        }

        if (string.IsNullOrWhiteSpace(uploadLocation) || !File.Exists(uploadLocation))
        {
            _logger.LogWarning("Upload location missing for dataset {DatasetId}", datasetId);
            dataset.Status = Contracts.Datasets.IngestionStatusDto.Failed;
            await _datasetRepository.UpdateAsync(dataset, cancellationToken);
            return;
        }

        try
        {
            dataset.Status = Contracts.Datasets.IngestionStatusDto.Processing;
            await _datasetRepository.UpdateAsync(dataset, cancellationToken);

            string fileToProcess = uploadLocation;
            string? tempExtractedPath = null;
            Dictionary<string, Dictionary<string, string>>? auxiliaryMetadata = null;
            
            // Check if uploaded file is a ZIP
            if (Path.GetExtension(uploadLocation).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Extracting ZIP file for dataset {DatasetId}", datasetId);
                
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
                _logger.LogInformation("Found primary file in ZIP: {FileName}", Path.GetFileName(primaryFile));

                string[] auxiliaryFiles = extractedFiles
                    .Where(f => !f.Equals(primaryFile, StringComparison.OrdinalIgnoreCase) &&
                                (f.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".tsv000", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".csv000", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (auxiliaryFiles.Length > 0)
                {
                    _logger.LogInformation("Found {Count} auxiliary metadata files: {Files}", auxiliaryFiles.Length,
                        string.Join(", ", auxiliaryFiles.Select(f => Path.GetRelativePath(tempExtractedPath, f))));
                    auxiliaryMetadata = await LoadAuxiliaryMetadataAsync(auxiliaryFiles, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("Found primary file in ZIP: {FileName}", Path.GetFileName(primaryFile));
                }
            }

            List<DatasetItemDto> parsedItems = await ParseUnsplashTsvAsync(fileToProcess, auxiliaryMetadata, cancellationToken);
            if (parsedItems.Count > 0)
            {
                await _datasetItemRepository.AddRangeAsync(datasetId, parsedItems, cancellationToken);
            }

            dataset.TotalItems = parsedItems.Count;
            dataset.Status = Contracts.Datasets.IngestionStatusDto.Completed;
            await _datasetRepository.UpdateAsync(dataset, cancellationToken);
            _logger.LogInformation("Ingestion completed for dataset {DatasetId} with {ItemCount} items", datasetId, parsedItems.Count);
            
            // Cleanup extracted files
            if (tempExtractedPath != null && Directory.Exists(tempExtractedPath))
            {
                try
                {
                    Directory.Delete(tempExtractedPath, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup temp extraction directory: {Path}", tempExtractedPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest dataset {DatasetId}", datasetId);
            dataset.Status = Contracts.Datasets.IngestionStatusDto.Failed;
            await _datasetRepository.UpdateAsync(dataset, cancellationToken);
        }
        finally
        {
            TryDeleteTempFile(uploadLocation);
        }
    }

    private async Task<List<DatasetItemDto>> ParseUnsplashTsvAsync(
        string filePath,
        Dictionary<string, Dictionary<string, string>>? auxiliaryMetadata,
        CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        _logger.LogInformation("ParseUnsplashTsvAsync: Read {LineCount} total lines from {FilePath}", lines.Length, Path.GetFileName(filePath));
        
        if (lines.Length <= 1)
        {
            return new List<DatasetItemDto>();
        }

        string[] headers = lines[0].Split('\t').Select(h => h.Trim()).ToArray();
        Dictionary<string, int> headerIndex = headers
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        string GetValue(string[] values, string column)
        {
            return headerIndex.TryGetValue(column, out int idx) && idx < values.Length
                ? values[idx].Trim()
                : string.Empty;
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
                _logger.LogDebug("Skipping row {RowIndex} due to column mismatch", i + 1);
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

        _logger.LogInformation("ParseUnsplashTsvAsync: Successfully parsed {ItemCount} items out of {TotalLines} lines", items.Count, lines.Length - 1);
        return items;
    }

    private void TryDeleteTempFile(string path)
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
            _logger.LogDebug(ex, "Failed to delete temp file {Path}", path);
        }
    }

    private async Task<Dictionary<string, Dictionary<string, string>>> LoadAuxiliaryMetadataAsync(
        IEnumerable<string> files,
        CancellationToken cancellationToken)
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
                    ? '\t'
                    : ',';

                string[] headers = lines[0].Split(separator).Select(h => h.Trim()).ToArray();
                _logger.LogInformation("Parsing metadata file {FileName} with columns: {Columns}", Path.GetFileName(file), string.Join(", ", headers));
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

                _logger.LogInformation("Loaded {EntryCount} rows from {FileName} (running distinct photo IDs: {Distinct})",
                    fileEntryCount,
                    Path.GetFileName(file),
                    aggregate.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse auxiliary metadata file {File}", file);
            }
        }

        return aggregate;
    }
}
