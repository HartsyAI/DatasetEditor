using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Configuration;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Production-ready service for ingesting datasets from multiple file formats.
/// Supports: CSV, TSV, JSON, JSONL, ZIP archives, image folders, and HuggingFace.
/// </summary>
public class DatasetIngestionService : IDatasetIngestionService
{
    private readonly Core.Abstractions.Repositories.IDatasetRepository _datasetRepository;
    private readonly Core.Abstractions.Repositories.IDatasetItemRepository _itemRepository;
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IConfiguration _configuration;
    private readonly string _uploadPath;
    private readonly string _datasetRootPath;

    public DatasetIngestionService(
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        Core.Abstractions.Repositories.IDatasetItemRepository itemRepository,
        IHuggingFaceClient huggingFaceClient,
        IConfiguration configuration)
    {
        _datasetRepository = datasetRepository ?? throw new ArgumentNullException(nameof(datasetRepository));
        _itemRepository = itemRepository ?? throw new ArgumentNullException(nameof(itemRepository));
        _huggingFaceClient = huggingFaceClient ?? throw new ArgumentNullException(nameof(huggingFaceClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";
        _datasetRootPath = configuration["Storage:DatasetRootPath"] ?? "./data/datasets";
    }

    public async Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadLocation) || !File.Exists(uploadLocation))
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, "Upload file not found", cancellationToken);
            throw new FileNotFoundException($"Upload file not found: {uploadLocation}");
        }

        using var fileStream = File.OpenRead(uploadLocation);
        var fileName = Path.GetFileName(uploadLocation);
        await IngestAsync(datasetId, fileStream, fileName, cancellationToken);
    }

    public async Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            Logs.Info($"[HF Import] Starting import for dataset {datasetId} from {request.Repository}");

            // If streaming mode, just update metadata - no download needed
            if (request.IsStreaming)
            {
                Logs.Info($"[HF Import] Streaming mode enabled for {request.Repository}");
                // Dataset metadata is already saved by the endpoint
                // Items will be fetched on-demand from HuggingFace Datasets Server API
                await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);
                Logs.Info($"[HF Import] Streaming dataset configured successfully");
                return;
            }

            // Non-streaming mode: Download and parse the dataset
            Logs.Info($"[HF Import] Download mode - fetching dataset info");
            var datasetInfo = await _huggingFaceClient.GetDatasetInfoAsync(
                request.Repository,
                request.Revision,
                request.AccessToken,
                cancellationToken);

            if (datasetInfo == null)
            {
                throw new InvalidOperationException($"Dataset {request.Repository} not found on HuggingFace Hub");
            }

            // Determine which file to download
            string? fileToDownload = request.DataFilePath;
            if (string.IsNullOrEmpty(fileToDownload))
            {
                // Try to find a parquet or CSV file automatically
                fileToDownload = datasetInfo.Files
                    .FirstOrDefault(f => f.Path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) ||
                                        f.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?.Path;

                if (string.IsNullOrEmpty(fileToDownload))
                {
                    throw new InvalidOperationException($"No suitable data file found in {request.Repository}. Please specify DataFilePath.");
                }
            }

            // Download the file
            var downloadPath = Path.Combine(_uploadPath, $"hf_{datasetId}_{Path.GetFileName(fileToDownload)}");
            Directory.CreateDirectory(_uploadPath);

            Logs.Info($"[HF Import] Downloading {fileToDownload} to {downloadPath}");
            await _huggingFaceClient.DownloadFileAsync(
                request.Repository,
                fileToDownload,
                downloadPath,
                request.Revision,
                request.AccessToken,
                cancellationToken);

            // Parse the downloaded file
            using var fileStream = File.OpenRead(downloadPath);
            await IngestAsync(datasetId, fileStream, Path.GetFileName(fileToDownload), cancellationToken);

            // Cleanup
            try
            {
                File.Delete(downloadPath);
            }
            catch (Exception ex)
            {
                Logs.Warning($"[HF Import] Failed to cleanup download file {downloadPath}: {ex.Message}");
            }

            Logs.Info($"[HF Import] Successfully imported dataset from {request.Repository}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[HF Import] Failed to import from HuggingFace: {ex.Message}");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task IngestAsync(Guid datasetId, Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            Logs.Info($"[Ingestion] Starting ingestion for dataset {datasetId}, file: {fileName}");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var items = new List<DatasetItemDto>();

            switch (extension)
            {
                case ".csv":
                    items = await ParseCsvAsync(datasetId, fileStream, cancellationToken);
                    break;

                case ".tsv":
                    items = await ParseTsvAsync(datasetId, fileStream, cancellationToken);
                    break;

                case ".json":
                    items = await ParseJsonAsync(datasetId, fileStream, cancellationToken);
                    break;

                case ".jsonl":
                case ".ndjson":
                    items = await ParseJsonLinesAsync(datasetId, fileStream, cancellationToken);
                    break;

                case ".zip":
                    items = await ParseZipAsync(datasetId, fileStream, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException($"File format '{extension}' is not supported");
            }

            if (items.Count == 0)
            {
                throw new InvalidOperationException("No items were parsed from the file");
            }

            // Write to Parquet
            await _itemRepository.InsertItemsAsync(datasetId, items, cancellationToken);

            // Update dataset metadata
            await _datasetRepository.UpdateItemCountAsync(datasetId, items.Count, cancellationToken);
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);

            Logs.Info($"[Ingestion] Successfully ingested {items.Count} items for dataset {datasetId}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[Ingestion] Failed to ingest dataset {datasetId}: {ex.Message}");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, ex.Message, cancellationToken);
            throw;
        }
    }

    public async Task IngestFromFolderAsync(Guid datasetId, string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            Logs.Info($"[Ingestion] Starting folder ingestion for dataset {datasetId}, folder: {folderPath}");

            var items = new List<DatasetItemDto>();
            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };

            var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (imageFiles.Count == 0)
            {
                throw new InvalidOperationException("No image files found in the specified folder");
            }

            foreach (var imagePath in imageFiles)
            {
                var relativePath = Path.GetRelativePath(folderPath, imagePath);
                var fileName = Path.GetFileName(imagePath);

                // Image dimensions can be populated later or by client
                int width = 0, height = 0;

                var item = new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = relativePath,
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    ImageUrl = $"file:///{imagePath.Replace("\\", "/")}",
                    Width = width,
                    Height = height,
                    Tags = new List<string>(),
                    IsFavorite = false,
                    Metadata = new Dictionary<string, string>
                    {
                        ["original_path"] = imagePath,
                        ["relative_path"] = relativePath
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                items.Add(item);
            }

            await _itemRepository.InsertItemsAsync(datasetId, items, cancellationToken);
            await _datasetRepository.UpdateItemCountAsync(datasetId, items.Count, cancellationToken);
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);

            Logs.Info($"[Ingestion] Successfully ingested {items.Count} images from folder for dataset {datasetId}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[Ingestion] Failed to ingest folder for dataset {datasetId}: {ex.Message}");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Parse CSV file (comma-delimited)
    /// </summary>
    private async Task<List<DatasetItemDto>> ParseCsvAsync(Guid datasetId, Stream stream, CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (await csv.ReadAsync())
        {
            var item = ParseRowToItem(datasetId, csv, headers);
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse TSV file (tab-delimited)
    /// </summary>
    private async Task<List<DatasetItemDto>> ParseTsvAsync(Guid datasetId, Stream stream, CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (await csv.ReadAsync())
        {
            var item = ParseRowToItem(datasetId, csv, headers);
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse JSON array file
    /// </summary>
    private async Task<List<DatasetItemDto>> ParseJsonAsync(Guid datasetId, Stream stream, CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();
        var jsonArray = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: cancellationToken);

        if (jsonArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("JSON file must contain an array of objects");
        }

        foreach (var element in jsonArray.EnumerateArray())
        {
            var item = ParseJsonElementToItem(datasetId, element);
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse JSONL/NDJSON file (newline-delimited JSON)
    /// </summary>
    private async Task<List<DatasetItemDto>> ParseJsonLinesAsync(Guid datasetId, Stream stream, CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var element = JsonSerializer.Deserialize<JsonElement>(line);
            var item = ParseJsonElementToItem(datasetId, element);
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse ZIP archive containing images
    /// </summary>
    private async Task<List<DatasetItemDto>> ParseZipAsync(Guid datasetId, Stream stream, CancellationToken cancellationToken)
    {
        var items = new List<DatasetItemDto>();
        var tempExtractPath = Path.Combine(_uploadPath, $"temp_{datasetId}");

        try
        {
            Directory.CreateDirectory(tempExtractPath);

            // Extract ZIP
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                archive.ExtractToDirectory(tempExtractPath, overwriteFiles: true);
            }

            // Process extracted images
            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
            var imageFiles = Directory.GetFiles(tempExtractPath, "*.*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            foreach (var imagePath in imageFiles)
            {
                var relativePath = Path.GetRelativePath(tempExtractPath, imagePath);
                var fileName = Path.GetFileName(imagePath);

                // Image dimensions can be populated later or by client
                int width = 0, height = 0;

                var item = new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = relativePath,
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    ImageUrl = $"file:///{imagePath.Replace("\\", "/")}",
                    Width = width,
                    Height = height,
                    Tags = new List<string>(),
                    IsFavorite = false,
                    Metadata = new Dictionary<string, string>
                    {
                        ["extracted_from_zip"] = "true",
                        ["original_path"] = relativePath
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                items.Add(item);
            }

            return items;
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(tempExtractPath))
            {
                try
                {
                    Directory.Delete(tempExtractPath, recursive: true);
                }
                catch (Exception ex)
                {
                    Logs.Warning($"[Ingestion] Failed to cleanup temp directory {tempExtractPath}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Parse CSV/TSV row to DatasetItemDto
    /// </summary>
    private DatasetItemDto ParseRowToItem(Guid datasetId, CsvReader csv, string[] headers)
    {
        var row = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            row[header.ToLowerInvariant()] = csv.GetField(header) ?? string.Empty;
        }

        // Try to find common column names
        var imageUrl = row.GetValueOrDefault("image_url")
            ?? row.GetValueOrDefault("imageurl")
            ?? row.GetValueOrDefault("url")
            ?? row.GetValueOrDefault("image")
            ?? string.Empty;

        var title = row.GetValueOrDefault("title")
            ?? row.GetValueOrDefault("name")
            ?? row.GetValueOrDefault("caption")
            ?? row.GetValueOrDefault("text")
            ?? $"Item {Guid.NewGuid()}";

        var description = row.GetValueOrDefault("description")
            ?? row.GetValueOrDefault("desc")
            ?? row.GetValueOrDefault("caption");

        var externalId = row.GetValueOrDefault("id")
            ?? row.GetValueOrDefault("image_id")
            ?? row.GetValueOrDefault("item_id")
            ?? Guid.NewGuid().ToString();

        int.TryParse(row.GetValueOrDefault("width") ?? "0", out var width);
        int.TryParse(row.GetValueOrDefault("height") ?? "0", out var height);

        var tags = new List<string>();
        if (row.TryGetValue("tags", out var tagsStr) && !string.IsNullOrEmpty(tagsStr))
        {
            tags = tagsStr.Split(',', ';').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        return new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            ExternalId = externalId,
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = row,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Parse JSON element to DatasetItemDto
    /// </summary>
    private DatasetItemDto ParseJsonElementToItem(Guid datasetId, JsonElement element)
    {
        var imageUrl = GetJsonString(element, "image_url", "imageUrl", "url", "image") ?? string.Empty;
        var title = GetJsonString(element, "title", "name", "caption", "text") ?? $"Item {Guid.NewGuid()}";
        var description = GetJsonString(element, "description", "desc", "caption");
        var externalId = GetJsonString(element, "id", "image_id", "item_id") ?? Guid.NewGuid().ToString();

        var width = GetJsonInt(element, "width");
        var height = GetJsonInt(element, "height");

        var tags = new List<string>();
        if (element.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            tags = tagsElement.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        var metadata = new Dictionary<string, string>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                metadata[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        return new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            ExternalId = externalId,
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private string? GetJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private int GetJsonInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return 0;
    }
}
