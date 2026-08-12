using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
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
///
/// Every source (file-based parsers below, and the folder scan) is a stream —
/// IAsyncEnumerable&lt;DatasetItemDto&gt; — so a multi-gigabyte CSV or a folder with
/// millions of images never sits fully materialized in memory. WriteInChunksAsync is
/// the single place that turns a stream into bounded-size batches and writes them.
/// </summary>
public class DatasetIngestionService : IDatasetIngestionService
{
    // Bounds parser-side memory (CSV row buffers, JSON element trees, extracted image
    // bytes) independently of ParquetSchemaDefinition.DefaultBatchSize, which governs
    // the writer's own row-group sizing.
    private const int ChunkSize = 2000;

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
        long totalWritten = 0;
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            Logs.Info($"[Ingestion] Starting ingestion for dataset {datasetId}, file: {fileName}");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            IAsyncEnumerable<DatasetItemDto> itemStream = extension switch
            {
                ".csv" => ParseCsvAsync(datasetId, fileStream, cancellationToken),
                ".tsv" => ParseTsvAsync(datasetId, fileStream, cancellationToken),
                ".json" => ParseJsonAsync(datasetId, fileStream, cancellationToken),
                ".jsonl" or ".ndjson" => ParseJsonLinesAsync(datasetId, fileStream, cancellationToken),
                ".zip" => ParseZipAsync(datasetId, fileStream, cancellationToken),
                _ => throw new NotSupportedException($"File format '{extension}' is not supported")
            };

            totalWritten = await WriteInChunksAsync(datasetId, itemStream, cancellationToken);

            if (totalWritten == 0)
            {
                throw new InvalidOperationException("No items were parsed from the file");
            }

            // Update dataset metadata with the TRUE total (not just this batch's count) — a
            // second ingestion into an existing dataset (re-upload, re-import) would otherwise
            // overwrite TotalItems with only the most recent batch's size, silently
            // understating the dataset's real, cumulative item count.
            long trueTotalCount = await _itemRepository.GetCountAsync(datasetId, cancellationToken);
            await _datasetRepository.UpdateItemCountAsync(datasetId, trueTotalCount, cancellationToken);
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);

            Logs.Info($"[Ingestion] Successfully ingested {totalWritten} items for dataset {datasetId} (total now {trueTotalCount})");
        }
        catch (Exception ex)
        {
            // Chunked writing means some chunks may already be committed to Parquet by the
            // time a later row/entry fails to parse -- unlike the old all-or-nothing behavior.
            // Deliberately NOT rolling back: deleting already-written items would also destroy
            // any items from a PRIOR successful ingestion into this same dataset (the
            // cumulative-count fix above exists precisely because re-ingestion into an existing
            // dataset is a supported case), and a partial-but-real dataset is more useful than
            // silently losing it. The honest thing is to leave what was written and say so.
            string message = totalWritten > 0
                ? $"Failed after writing {totalWritten} item(s): {ex.Message}"
                : ex.Message;
            Logs.Error($"[Ingestion] Failed to ingest dataset {datasetId}: {message}");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Drains an item stream in fixed-size batches, writing (and thus committing to Parquet)
    /// each batch as soon as it fills, rather than buffering the whole source first. Shared by
    /// every ingestion source (file parsers, folder scan) so they get identical chunking and
    /// progress-logging behavior.
    /// </summary>
    private async Task<long> WriteInChunksAsync(Guid datasetId, IAsyncEnumerable<DatasetItemDto> items, CancellationToken cancellationToken)
    {
        long totalWritten = 0;
        var chunk = new List<DatasetItemDto>(ChunkSize);

        await foreach (DatasetItemDto item in items.WithCancellation(cancellationToken))
        {
            chunk.Add(item);
            if (chunk.Count >= ChunkSize)
            {
                await _itemRepository.InsertItemsAsync(datasetId, chunk, cancellationToken);
                totalWritten += chunk.Count;
                Logs.Info($"[Ingestion] Wrote chunk of {chunk.Count} items for dataset {datasetId} (total so far: {totalWritten})");
                chunk.Clear();
            }
        }

        if (chunk.Count > 0)
        {
            await _itemRepository.InsertItemsAsync(datasetId, chunk, cancellationToken);
            totalWritten += chunk.Count;
        }

        return totalWritten;
    }

    public async Task IngestFromFolderAsync(Guid datasetId, string folderPath, CancellationToken cancellationToken = default)
    {
        long totalWritten = 0;
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            Logs.Info($"[Ingestion] Starting folder ingestion for dataset {datasetId}, folder: {folderPath}");

            // Record where the source images actually live so ServeDatasetFile knows to serve
            // straight from this folder — a dataset here can be an arbitrary, potentially huge
            // folder on disk, and copying it into managed storage would defeat the point of
            // "point at a folder" ingestion (and contradicts the flat-memory/no-materialization
            // design elsewhere in this app).
            DatasetDto? dataset = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken);
            if (dataset is null)
            {
                throw new InvalidOperationException($"Dataset {datasetId} not found");
            }
            string normalizedFolderPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(normalizedFolderPath))
            {
                throw new InvalidOperationException($"Folder not found: {normalizedFolderPath}");
            }
            await _datasetRepository.UpdateAsync(dataset with { StoragePath = normalizedFolderPath }, cancellationToken);

            totalWritten = await WriteInChunksAsync(
                datasetId, EnumerateFolderImagesAsync(datasetId, normalizedFolderPath, cancellationToken), cancellationToken);

            if (totalWritten == 0)
            {
                throw new InvalidOperationException("No image files found in the specified folder");
            }

            // See IngestAsync's matching comment: use the true cumulative count, not this batch's.
            long trueTotalCount = await _itemRepository.GetCountAsync(datasetId, cancellationToken);
            await _datasetRepository.UpdateItemCountAsync(datasetId, trueTotalCount, cancellationToken);
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);

            Logs.Info($"[Ingestion] Successfully ingested {totalWritten} images from folder for dataset {datasetId} (total now {trueTotalCount})");
        }
        catch (Exception ex)
        {
            // Same partial-write policy as IngestAsync -- see its catch block for the reasoning.
            string message = totalWritten > 0
                ? $"Failed after writing {totalWritten} item(s): {ex.Message}"
                : ex.Message;
            Logs.Error($"[Ingestion] Failed to ingest folder for dataset {datasetId}: {message}");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Lazily walks a folder for supported image files, yielding one item per match.
    /// Directory.EnumerateFiles (not GetFiles) so a folder with millions of images is never
    /// materialized into one big list before the first item is even written.
    /// </summary>
    private async IAsyncEnumerable<DatasetItemDto> EnumerateFolderImagesAsync(
        Guid datasetId, string normalizedFolderPath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };

        foreach (var imagePath in Directory.EnumerateFiles(normalizedFolderPath, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!supportedExtensions.Contains(Path.GetExtension(imagePath).ToLowerInvariant()))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(normalizedFolderPath, imagePath);
            var fileName = Path.GetFileName(imagePath);
            // Server-relative API URL (ImageUrlHelper.ResolveImageUrl prepends the API base
            // address client-side) — NOT a file:// URI, which points at the server's own
            // filesystem and a browser has no way to fetch.
            var apiUrl = BuildServeFileUrl(datasetId, relativePath);

            yield return new DatasetItemDto
            {
                Id = Guid.NewGuid(),
                DatasetId = datasetId,
                ExternalId = relativePath,
                Title = Path.GetFileNameWithoutExtension(fileName),
                ImageUrl = apiUrl,
                ThumbnailUrl = apiUrl,
                // Image dimensions can be populated later or by client
                Width = 0,
                Height = 0,
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
        }
    }

    /// <summary>
    /// Parse CSV file (comma-delimited)
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseCsvAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
            cancellationToken.ThrowIfCancellationRequested();
            yield return ParseRowToItem(datasetId, csv, headers);
        }
    }

    /// <summary>
    /// Parse TSV file (tab-delimited)
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseTsvAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
            cancellationToken.ThrowIfCancellationRequested();
            yield return ParseRowToItem(datasetId, csv, headers);
        }
    }

    /// <summary>
    /// Parse JSON array file. JsonSerializer.DeserializeAsyncEnumerable requires (and enforces)
    /// a top-level array — a non-array root surfaces as a JsonException during enumeration,
    /// which IngestAsync's catch turns into a clear Failed status instead of silently buffering
    /// the whole document the way DeserializeAsync&lt;JsonElement&gt; used to.
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseJsonAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<JsonElement> elements = JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken);
        await foreach (JsonElement element in elements.WithCancellation(cancellationToken))
        {
            // Defensive: a JSON array can legally contain non-object entries (numbers, strings,
            // null) even though every real dataset export uses objects. Skip rather than crash
            // on EnumerateObject() -- streaming exposes us to messier real-world input than the
            // old fully-materialized path effectively did before anyone tried a 50k-row file.
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            yield return ParseJsonElementToItem(datasetId, element);
        }
    }

    /// <summary>
    /// Parse JSONL/NDJSON file (newline-delimited JSON)
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseJsonLinesAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var element = JsonSerializer.Deserialize<JsonElement>(line);
            yield return ParseJsonElementToItem(datasetId, element);
        }
    }

    /// <summary>
    /// Parse ZIP archive containing images, extracting and yielding entries one at a time
    /// instead of extracting the whole archive before building the item list.
    ///
    /// This can't be a single try/catch around the yielding loop -- C# forbids `yield return`
    /// inside a try block that has a catch clause (CS1626). Cleanup-on-failure is done via
    /// try/finally instead (legal around yield return): `completedSuccessfully` only flips to
    /// true after the loop finishes, so any exit that isn't a clean finish -- an exception, or
    /// the caller disposing the enumerator early via cancellation -- deletes the partial
    /// extraction, same as the old catch block did.
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseZipAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Extract into the dataset's own managed storage folder (ServeDatasetFile's
        // {root}/{slug}-{shortId} convention) — NOT a temp directory. This used to extract to
        // _uploadPath/temp_{datasetId} and then delete it in `finally` on every run, so a
        // ZIP-imported dataset's images were deleted immediately after being catalogued and
        // could never actually be served, regardless of what URL scheme they were given.
        DatasetDto? dataset = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            throw new InvalidOperationException($"Dataset {datasetId} not found");
        }
        var extractPath = Services.Storage.DatasetStorageConventions.GetDatasetFolderPath(_datasetRootPath, datasetId, dataset.Name);
        Directory.CreateDirectory(extractPath);
        string normalizedExtractRoot = Path.GetFullPath(extractPath);
        string extractContainmentPrefix = normalizedExtractRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedExtractRoot
            : normalizedExtractRoot + Path.DirectorySeparatorChar;
        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };

        bool completedSuccessfully = false;
        try
        {
            // Extract entry-by-entry with an explicit containment check per entry, rather than
            // the one-line ZipArchive.ExtractToDirectory. .NET's own extraction already rejects
            // ".."-based zip-slip, and doesn't reconstruct Unix symlink entries as real OS
            // symlinks (it writes their "content" — which a symlink-aware tool would treat as
            // the link target — as a plain file) — but validating containment explicitly here
            // doesn't depend on either of those framework internals holding across versions,
            // and matches the same trailing-separator-safe check used when serving these files
            // back out (ServeDatasetFile). Only image entries get extracted at all (rather than
            // extracting everything and filtering afterward) — one pass, no dead files left on
            // disk for non-image archive members.
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // directory entry
                }
                if (!supportedExtensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()))
                {
                    continue; // not a supported image type
                }

                string destinationPath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, entry.FullName));
                if (!destinationPath.StartsWith(extractContainmentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Logs.Warning($"[Ingestion] Skipping ZIP entry that would extract outside the target directory: {entry.FullName}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);

                string relativePath = Path.GetRelativePath(normalizedExtractRoot, destinationPath);
                string fileName = Path.GetFileName(destinationPath);
                yield return new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = relativePath,
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    ImageUrl = BuildServeFileUrl(datasetId, relativePath),
                    ThumbnailUrl = BuildServeFileUrl(datasetId, relativePath),
                    Width = 0,
                    Height = 0,
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
            }
            completedSuccessfully = true;
        }
        finally
        {
            if (!completedSuccessfully && Directory.Exists(extractPath))
            {
                try
                {
                    Directory.Delete(extractPath, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    Logs.Warning($"[Ingestion] Failed to clean up partially-extracted ZIP contents at {extractPath}: {cleanupEx.Message}");
                }
            }
        }
    }

    /// <summary>Builds the server-relative API URL ServeDatasetFile will resolve a stored file from.</summary>
    private static string BuildServeFileUrl(Guid datasetId, string relativePath)
    {
        string encodedPath = string.Join('/', relativePath
            .Replace('\\', '/')
            .Split('/')
            .Select(Uri.EscapeDataString));
        return $"/api/datasets/{datasetId}/files/{encodedPath}";
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
