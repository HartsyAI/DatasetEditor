using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement.Parsing;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

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
    // Each chunk is appended as one Parquet row group, so this is the row-group size of every
    // ingested shard. It was 2,000 while the writer's batch size was 10,000, so shards filled up
    // with five times as many small groups as intended (audit I-9, P-5). Items carry metadata
    // only, never image bytes, so 10,000 of them is a few megabytes.
    private const int ChunkSize = DataAccess.Parquet.ParquetSchemaDefinition.DefaultBatchSize;

    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetItemRepository _itemRepository;
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IHuggingFaceDatasetServerClient _huggingFaceDatasetServerClient;
    private readonly IConfiguration _configuration;
    private readonly string _uploadPath;
    private readonly string _datasetRootPath;
    private readonly ILogger<DatasetIngestionService> _logger;

    public DatasetIngestionService(
        IDatasetRepository datasetRepository,
        IDatasetItemRepository itemRepository,
        IHuggingFaceClient huggingFaceClient,
        IHuggingFaceDatasetServerClient huggingFaceDatasetServerClient,
        IConfiguration configuration,
        ILogger<DatasetIngestionService> logger)
    {
        _datasetRepository = datasetRepository ?? throw new ArgumentNullException(nameof(datasetRepository));
        _itemRepository = itemRepository ?? throw new ArgumentNullException(nameof(itemRepository));
        _huggingFaceClient = huggingFaceClient ?? throw new ArgumentNullException(nameof(huggingFaceClient));
        _huggingFaceDatasetServerClient = huggingFaceDatasetServerClient ?? throw new ArgumentNullException(nameof(huggingFaceDatasetServerClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";
        _datasetRootPath = configuration["Storage:DatasetRootPath"] ?? "./data/datasets";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadLocation) || !File.Exists(uploadLocation))
        {
            // Failure writes use a non-cancellable token: a cancelled request/shutdown would
            // otherwise make the status write itself throw, leaving the dataset stuck in Processing (audit I-2).
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, "Upload file not found", CancellationToken.None);
            throw new FileNotFoundException($"Upload file not found: {uploadLocation}");
        }

        using var fileStream = File.OpenRead(uploadLocation);
        var fileName = Path.GetFileName(uploadLocation);
        await IngestAsync(datasetId, fileStream, fileName, cancellationToken);
    }

    /// <summary>
    /// Chooses the config/split a streaming import counts. A requested split must match exactly
    /// within the requested config. It is never swapped for another config's split, because the match
    /// is persisted as the dataset's source. With no split given, the default is the "train" split of
    /// the requested config, or the first split datasets-server lists.
    /// </summary>
    private static HuggingFaceDatasetSplitInfo? PickStreamingSplit(
        IReadOnlyList<HuggingFaceDatasetSplitInfo>? splits,
        string? config,
        string? split)
    {
        if (splits is null || splits.Count == 0)
        {
            return null;
        }

        IEnumerable<HuggingFaceDatasetSplitInfo> inConfig = string.IsNullOrWhiteSpace(config)
            ? splits
            : splits.Where(s => string.Equals(s.Config, config, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(split))
        {
            return inConfig.FirstOrDefault(s => string.Equals(s.Split, split, StringComparison.Ordinal));
        }

        return inConfig.FirstOrDefault(s => string.Equals(s.Split, "train", StringComparison.Ordinal))
            ?? inConfig.FirstOrDefault();
    }

    /// <summary>Runs an import and, for streaming datasets, records the default config/split and its row count.</summary>
    public async Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            _logger.LogInformation("[HF Import] Starting import for dataset {DatasetId} from {Repository}", datasetId, request.Repository);

            // If streaming mode, just update metadata - no download needed
            if (request.IsStreaming)
            {
                _logger.LogInformation("[HF Import] Streaming mode enabled for {Repository}", request.Repository);
                // Dataset metadata is already saved by the endpoint
                // Items will be fetched on-demand from HuggingFace Datasets Server API
                await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Completed, cancellationToken: cancellationToken);

                // Row count isn't known from the import request itself -- resolve it against
                // the pinned config/split so the UI doesn't show "0 items" for a dataset that's
                // actually fully browsable. Best-effort: a lookup failure (renamed/gated repo,
                // datasets-server outage) leaves the count unset rather than failing an import
                // that already succeeded.
                try
                {
                    List<HuggingFaceDatasetSplitInfo>? splits = await _huggingFaceDatasetServerClient.GetAllSplitsAsync(
                        request.Repository, request.AccessToken, cancellationToken);
                    // A null config/split means "use the default". Matching it with Ordinal equality never
                    // succeeded, so the count was silently skipped (audit H-1(1)).
                    HuggingFaceDatasetSplitInfo? match = PickStreamingSplit(splits, request.Config, request.Split);

                    if (match != null)
                    {
                        // Persist the resolved config/split. The items endpoint streams from these fields;
                        // leaving them unset would make every page request re-run /size discovery.
                        DatasetRecord? current = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken);
                        if (current != null)
                        {
                            await _datasetRepository.UpdateAsync(
                                current,
                                current with { HuggingFaceConfig = match.Config, HuggingFaceSplit = match.Split },
                                cancellationToken);
                        }
                    }

                    if (match is { NumRows: > 0 })
                    {
                        await _datasetRepository.UpdateItemCountAsync(datasetId, match.NumRows, cancellationToken);
                        _logger.LogInformation("[HF Import] Resolved streaming row count for {Repository} ({Config}/{Split}): {NumRows}",
                            request.Repository, request.Config, request.Split, match.NumRows);
                    }
                    else
                    {
                        _logger.LogWarning("[HF Import] Could not resolve row count for streaming dataset {Repository} ({Config}/{Split})",
                            request.Repository, request.Config, request.Split);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[HF Import] Row-count lookup failed for streaming dataset {Repository}; leaving count unset", request.Repository);
                }

                _logger.LogInformation("[HF Import] Streaming dataset configured successfully");
                return;
            }

            // Non-streaming mode: Download and parse the dataset
            _logger.LogInformation("[HF Import] Download mode - fetching dataset info");
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
            HuggingFaceDatasetFile? listedFile = string.IsNullOrEmpty(fileToDownload)
                ? PickDownloadFile(datasetInfo.Files)
                : datasetInfo.Files.FirstOrDefault(f => string.Equals(f.Path, fileToDownload, StringComparison.Ordinal));
            // An empty DataFilePath means "pick one" too, as it always did (Codex review on #20).
            if (string.IsNullOrEmpty(fileToDownload))
            {
                fileToDownload = listedFile?.Path;
            }
            if (string.IsNullOrEmpty(fileToDownload))
            {
                throw new InvalidOperationException($"No suitable data file found in {request.Repository}. Please specify DataFilePath.");
            }

            // Refuse an oversized file before fetching any of it; the client also enforces the
            // limit while streaming, for files the tree didn't size (audit H-4).
            long maxDownloadBytes = MaxDownloadBytes;
            if (listedFile is { Size: > 0 } && listedFile.Size > maxDownloadBytes)
            {
                throw new InvalidOperationException(HuggingFaceClient.DownloadTooLargeMessage(fileToDownload, maxDownloadBytes));
            }

            // Download the file
            var downloadPath = Path.Combine(_uploadPath, $"hf_{datasetId}_{Path.GetFileName(fileToDownload)}");
            Directory.CreateDirectory(_uploadPath);

            try
            {
                _logger.LogInformation("[HF Import] Downloading {FileToDownload} to {DownloadPath}", fileToDownload, downloadPath);
                await _huggingFaceClient.DownloadFileAsync(
                    request.Repository,
                    fileToDownload,
                    downloadPath,
                    request.Revision,
                    request.AccessToken,
                    maxDownloadBytes,
                    cancellationToken);

                // Parse the downloaded file
                using var fileStream = File.OpenRead(downloadPath);
                await IngestAsync(datasetId, fileStream, Path.GetFileName(fileToDownload), cancellationToken);
            }
            finally
            {
                // Deleted on failure too: a parse error used to leave uploads/hf_* behind (audit I-8).
                TryDeleteFile(downloadPath);
            }

            _logger.LogInformation("[HF Import] Successfully imported dataset from {Repository}", request.Repository);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HF Import] Failed to import from HuggingFace");
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task IngestAsync(Guid datasetId, Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        long totalWritten = 0;
        try
        {
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Processing, cancellationToken: cancellationToken);
            _logger.LogInformation("[Ingestion] Starting ingestion for dataset {DatasetId}, file: {FileName}", datasetId, fileName);

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            IAsyncEnumerable<DatasetItemDto> itemStream = extension switch
            {
                ".csv" => ParseCsvAsync(datasetId, fileStream, cancellationToken),
                ".tsv" => ParseTsvAsync(datasetId, fileStream, cancellationToken),
                ".json" => ParseJsonAsync(datasetId, fileStream, cancellationToken),
                ".jsonl" or ".ndjson" => ParseJsonLinesAsync(datasetId, fileStream, cancellationToken),
                ".zip" => ParseZipAsync(datasetId, fileStream, cancellationToken),
                ".parquet" => ParseParquetAsync(datasetId, fileStream, cancellationToken),
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

            _logger.LogInformation("[Ingestion] Successfully ingested {TotalWritten} items for dataset {DatasetId} (total now {TrueTotalCount})", totalWritten, datasetId, trueTotalCount);
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
            _logger.LogError(ex, "[Ingestion] Failed to ingest dataset {DatasetId}: {Message}", datasetId, message);
            // Non-cancellable: this is the write that records the failure, so it must land even when
            // the cancellation being handled is the reason for the failure (audit I-2).
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Drains an item stream in fixed-size batches, writing (and thus committing to Parquet)
    /// each batch as soon as it fills, rather than buffering the whole source first. Shared by
    /// every ingestion source (file parsers, folder scan) so they get identical chunking and
    /// progress-logging behavior. After each chunk the dataset's item count is updated, so the
    /// client's status poll shows progress during a long import instead of 0 until the end
    /// (audit I-3).
    /// </summary>
    private async Task<long> WriteInChunksAsync(Guid datasetId, IAsyncEnumerable<DatasetItemDto> items, CancellationToken cancellationToken)
    {
        long totalWritten = 0;
        long itemsBefore = await _itemRepository.GetCountAsync(datasetId, cancellationToken);
        var chunk = new List<DatasetItemDto>(ChunkSize);

        await foreach (DatasetItemDto item in items.WithCancellation(cancellationToken))
        {
            chunk.Add(item);
            if (chunk.Count >= ChunkSize)
            {
                await _itemRepository.AddRangeAsync(datasetId, chunk, cancellationToken);
                totalWritten += chunk.Count;
                _logger.LogInformation("[Ingestion] Wrote chunk of {ChunkCount} items for dataset {DatasetId} (total so far: {TotalWritten})", chunk.Count, datasetId, totalWritten);
                chunk.Clear();
                await _datasetRepository.UpdateItemCountAsync(datasetId, itemsBefore + totalWritten, cancellationToken);
            }
        }

        if (chunk.Count > 0)
        {
            await _itemRepository.AddRangeAsync(datasetId, chunk, cancellationToken);
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
            _logger.LogInformation("[Ingestion] Starting folder ingestion for dataset {DatasetId}, folder: {FolderPath}", datasetId, folderPath);

            // Record where the source images actually live so ServeDatasetFile knows to serve
            // straight from this folder — a dataset here can be an arbitrary, potentially huge
            // folder on disk, and copying it into managed storage would defeat the point of
            // "point at a folder" ingestion (and contradicts the flat-memory/no-materialization
            // design elsewhere in this app).
            DatasetRecord? dataset = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken);
            if (dataset is null)
            {
                throw new InvalidOperationException($"Dataset {datasetId} not found");
            }
            string normalizedFolderPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(normalizedFolderPath))
            {
                throw new InvalidOperationException($"Folder not found: {normalizedFolderPath}");
            }
            // SourceType used to stay LocalUpload, so the library labelled a server folder as an
            // uploaded file and its source filter couldn't find it (audit F-3).
            await _datasetRepository.UpdateAsync(
                dataset,
                dataset with { StoragePath = normalizedFolderPath, SourceType = DatasetSourceType.LocalFolder },
                cancellationToken);

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

            _logger.LogInformation("[Ingestion] Successfully ingested {TotalWritten} images from folder for dataset {DatasetId} (total now {TrueTotalCount})", totalWritten, datasetId, trueTotalCount);
        }
        catch (Exception ex)
        {
            // Same partial-write policy as IngestAsync -- see its catch block for the reasoning.
            string message = totalWritten > 0
                ? $"Failed after writing {totalWritten} item(s): {ex.Message}"
                : ex.Message;
            _logger.LogError(ex, "[Ingestion] Failed to ingest folder for dataset {DatasetId}: {Message}", datasetId, message);
            // Non-cancellable for the same reason as IngestAsync's failure write (audit I-2).
            await _datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message, CancellationToken.None);
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
        foreach (var imagePath in Directory.EnumerateFiles(normalizedFolderPath, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ImageFiles.IsSupported(imagePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(normalizedFolderPath, imagePath);
            var fileName = Path.GetFileName(imagePath);
            // Server-relative API URL (ImageUrlHelper.ResolveImageUrl prepends the API base
            // address client-side) — NOT a file:// URI, which points at the server's own
            // filesystem and a browser has no way to fetch.
            var apiUrl = BuildServeFileUrl(datasetId, relativePath);
            // Header only, no pixel decode. Items used to be stored 0x0 (audit I-10).
            (int width, int height) = ImageFiles.ReadDimensions(imagePath);

            yield return new DatasetItemDto
            {
                Id = Guid.NewGuid(),
                DatasetId = datasetId,
                ExternalId = relativePath,
                Title = Path.GetFileNameWithoutExtension(fileName),
                ImageUrl = apiUrl,
                ThumbnailUrl = apiUrl,
                Width = width,
                Height = height,
                Tags = new List<string>(),
                IsFavorite = false,
                // Relative only: the absolute server path (original_path) was shown to every
                // reader of the dataset (audit I-10). The dataset's StoragePath holds the root.
                Metadata = new Dictionary<string, string>
                {
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
        DatasetRecord? dataset = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken);
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
            // back out (ServeDatasetFile). Only image entries are extracted; data files are
            // read straight from the archive.
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);

            // Pass 1: extract images and note data files. Images are listed after the data rows,
            // because a row may describe an image (and then the image isn't listed on its own).
            List<string> extractedImages = new();
            List<ZipArchiveEntry> dataEntries = new();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // directory entry
                }
                if (IsZipDataFile(entry.Name))
                {
                    dataEntries.Add(entry);
                    continue;
                }
                if (!ImageFiles.IsSupported(entry.Name))
                {
                    continue;
                }

                string destinationPath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, entry.FullName));
                if (!destinationPath.StartsWith(extractContainmentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[Ingestion] Skipping ZIP entry that would extract outside the target directory: {EntryFullName}", entry.FullName);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
                extractedImages.Add(Path.GetRelativePath(normalizedExtractRoot, destinationPath).Replace('\\', '/'));
            }

            // Pass 2: rows from data files inside the archive. A ZIP of CSVs used to ingest 0
            // items (audit I-6). A row that names an extracted image (HuggingFace imagefolder's
            // metadata.csv/metadata.jsonl `file_name`, or a relative path in an image column)
            // is linked to it.
            // Exact archive paths: on a case-sensitive filesystem A.png and a.png are two images.
            ZipImageIndex imageSet = new(extractedImages);
            HashSet<string> describedImages = new(StringComparer.Ordinal);
            foreach (ZipArchiveEntry dataEntry in dataEntries)
            {
                string entryFolder = (Path.GetDirectoryName(dataEntry.FullName) ?? string.Empty).Replace('\\', '/');
                await foreach (DatasetItemDto row in ParseZipDataEntryAsync(datasetId, dataEntry, cancellationToken))
                {
                    string? linkedImage = FindLinkedImage(row, entryFolder, imageSet);
                    if (linkedImage is null)
                    {
                        yield return row;
                        continue;
                    }

                    describedImages.Add(linkedImage);
                    string url = BuildServeFileUrl(datasetId, linkedImage);
                    (int width, int height) = row.Width > 0 && row.Height > 0
                        ? (row.Width, row.Height)
                        : ImageFiles.ReadDimensions(Path.Combine(normalizedExtractRoot, linkedImage));
                    yield return row with { ImageUrl = url, ThumbnailUrl = url, Width = width, Height = height };
                }
            }

            // Pass 3: every image no row described becomes an item of its own, as before.
            foreach (string relativePath in extractedImages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (describedImages.Contains(relativePath))
                {
                    continue;
                }

                string url = BuildServeFileUrl(datasetId, relativePath);
                (int width, int height) = ImageFiles.ReadDimensions(Path.Combine(normalizedExtractRoot, relativePath));
                yield return new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = relativePath,
                    Title = Path.GetFileNameWithoutExtension(relativePath),
                    ImageUrl = url,
                    ThumbnailUrl = url,
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
                    _logger.LogWarning(cleanupEx, "[Ingestion] Failed to clean up partially-extracted ZIP contents at {ExtractPath}", extractPath);
                }
            }
        }
    }

    /// <summary>Data files read from inside a ZIP, by extension.</summary>
    private static bool IsZipDataFile(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() is ".csv" or ".tsv" or ".json" or ".jsonl" or ".ndjson" or ".parquet";
    }

    /// <summary>Parses one data file inside a ZIP with the parser for its extension.</summary>
    private async IAsyncEnumerable<DatasetItemDto> ParseZipDataEntryAsync(
        Guid datasetId, ZipArchiveEntry entry, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (extension == ".parquet")
        {
            // Parquet's footer is at the end of the file, so it needs a seekable stream, which a
            // ZIP entry isn't. Copy it to the staging folder first.
            Directory.CreateDirectory(_uploadPath);
            string tempPath = Path.Combine(_uploadPath, $"zip_{datasetId:N}_{Guid.NewGuid():N}.parquet");
            try
            {
                await using (Stream source = entry.Open())
                await using (FileStream target = File.Create(tempPath))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                await using FileStream parquet = File.OpenRead(tempPath);
                await foreach (DatasetItemDto item in ParseParquetAsync(datasetId, parquet, cancellationToken))
                {
                    yield return item;
                }
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
            yield break;
        }

        await using Stream entryStream = entry.Open();
        IAsyncEnumerable<DatasetItemDto> items = extension switch
        {
            ".csv" => ParseCsvAsync(datasetId, entryStream, cancellationToken),
            ".tsv" => ParseTsvAsync(datasetId, entryStream, cancellationToken),
            ".json" => ParseJsonAsync(datasetId, entryStream, cancellationToken),
            _ => ParseJsonLinesAsync(datasetId, entryStream, cancellationToken)
        };
        await foreach (DatasetItemDto item in items.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// The extracted image a row refers to: its <c>file_name</c> (HuggingFace imagefolder) or a
    /// relative path in its image column, resolved against the data file's folder first.
    /// </summary>
    private static string? FindLinkedImage(DatasetItemDto row, string dataFileFolder, ZipImageIndex extractedImages)
    {
        List<string> candidates = new();
        foreach (KeyValuePair<string, string> pair in row.Metadata)
        {
            bool isPathColumn = string.Equals(pair.Key, "file_name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Key, "image_file_name", StringComparison.Ordinal);
            if (isPathColumn && !string.IsNullOrWhiteSpace(pair.Value))
            {
                candidates.Add(pair.Value);
            }
        }
        if (!string.IsNullOrWhiteSpace(row.ImageUrl) && !row.ImageUrl.Contains("://", StringComparison.Ordinal))
        {
            candidates.Add(row.ImageUrl);
        }

        foreach (string candidate in candidates)
        {
            string relative = candidate.Replace('\\', '/');
            if (dataFileFolder.Length > 0 && NormalizeArchivePath($"{dataFileFolder}/{relative}") is { } besideDataFile
                && extractedImages.Find(besideDataFile) is { } nearMatch)
            {
                return nearMatch;
            }
            if (NormalizeArchivePath(relative) is { } fromRoot && extractedImages.Find(fromRoot) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves "." and ".." segments in a path inside the archive, so "metadata/../images/a.png"
    /// finds "images/a.png" (Codex review on #20). Returns null for a path that climbs above the
    /// archive root: nothing outside the extraction folder can be linked.
    /// </summary>
    internal static string? NormalizeArchivePath(string path)
    {
        Stack<string> segments = new();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }
                segments.Pop();
                continue;
            }
            segments.Push(segment);
        }
        return segments.Count == 0 ? null : string.Join('/', segments.Reverse());
    }

    /// <summary>
    /// Looks up extracted ZIP images by archive path: an exact match first, then a
    /// case-insensitive one only when exactly one image matches. A plain case-insensitive set
    /// merged A.png and a.png, so a row naming either got the other's image and one image was
    /// dropped from the dataset (Codex review on #20).
    /// </summary>
    private sealed class ZipImageIndex
    {
        private readonly HashSet<string> _exact;
        private readonly ILookup<string, string> _ignoringCase;

        public ZipImageIndex(IEnumerable<string> paths)
        {
            List<string> all = paths.ToList();
            _exact = new HashSet<string>(all, StringComparer.Ordinal);
            _ignoringCase = all.Distinct(StringComparer.Ordinal).ToLookup(p => p, StringComparer.OrdinalIgnoreCase);
        }

        public string? Find(string path)
        {
            if (_exact.Contains(path))
            {
                return path;
            }
            List<string> matches = _ignoringCase[path].ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
    }

    /// <summary>
    /// Parses a Parquet file (audit I-6). Images embedded in the file (HuggingFace's
    /// <c>image: {bytes, path}</c>) are written to the dataset's storage folder and served from
    /// there, like images extracted from a ZIP.
    /// </summary>
    internal async IAsyncEnumerable<DatasetItemDto> ParseParquetAsync(
        Guid datasetId, Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? imageRoot = null;
        await foreach (ParquetRow row in ParquetRowReader.ReadAsync(stream, cancellationToken))
        {
            DatasetItemDto item = DatasetRowMapper.Map(datasetId, row.Values);
            // Kept whether or not there are bytes: an image struct with only a path refers to a
            // file shipped beside the Parquet (in a ZIP), which linking resolves from this
            // (Codex review on #20).
            if (!string.IsNullOrWhiteSpace(row.ImageName))
            {
                item.Metadata["image_file_name"] = row.ImageName;
            }

            string? extension = row.ImageBytes is { Length: > 0 } bytes ? ImageFiles.SniffExtension(bytes) : null;
            if (extension is null)
            {
                yield return item;
                continue;
            }

            imageRoot ??= await GetDatasetFolderAsync(datasetId, cancellationToken);
            // A fresh name per image, so importing a second file into the dataset never
            // overwrites images the first one wrote.
            string relativePath = $"images/{Guid.NewGuid():N}{extension}";
            string fullPath = Path.Combine(imageRoot, "images", Path.GetFileName(relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, row.ImageBytes!, cancellationToken);

            (int width, int height) = item.Width > 0 && item.Height > 0
                ? (item.Width, item.Height)
                : ImageFiles.ReadDimensions(row.ImageBytes!);
            string url = BuildServeFileUrl(datasetId, relativePath);
            yield return item with { ImageUrl = url, ThumbnailUrl = url, Width = width, Height = height };
        }
    }

    /// <summary>The dataset's managed storage folder (ServeDatasetFile's {root}/{slug}-{shortId}).</summary>
    private async Task<string> GetDatasetFolderAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        DatasetRecord? dataset = await _datasetRepository.GetByIdAsync(datasetId, cancellationToken)
            ?? throw new InvalidOperationException($"Dataset {datasetId} not found");
        string folder = Path.GetFullPath(Services.Storage.DatasetStorageConventions.GetDatasetFolderPath(_datasetRootPath, datasetId, dataset.Name));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Largest file HuggingFace download mode fetches (<c>Ingestion:MaxDownloadBytes</c>), by
    /// default the same 5 GB an upload may be (audit H-4).
    /// </summary>
    private long MaxDownloadBytes =>
        _configuration.GetValue<long?>("Ingestion:MaxDownloadBytes") ?? Configuration.UploadRequestBodyLimit.MaxUploadBodyBytes;

    /// <summary>
    /// The repository file download mode imports when the request doesn't name one: a format
    /// this service parses, Parquet first (HuggingFace's own conversion format), preferring a
    /// train split.
    /// </summary>
    internal static HuggingFaceDatasetFile? PickDownloadFile(IEnumerable<HuggingFaceDatasetFile> files)
    {
        string[] preference = { ".parquet", ".jsonl", ".ndjson", ".csv", ".tsv", ".json" };
        return files
            .Select(f => (File: f, Rank: Array.IndexOf(preference, Path.GetExtension(f.Path).ToLowerInvariant())))
            .Where(c => c.Rank >= 0)
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.File.Path.Contains("train", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.File.Path, StringComparer.Ordinal)
            .Select(c => c.File)
            .FirstOrDefault();
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Ingestion] Could not delete temporary file {Path}", path);
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
    private static DatasetItemDto ParseRowToItem(Guid datasetId, CsvReader csv, string[] headers)
    {
        var row = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            row[header.ToLowerInvariant()] = csv.GetField(header) ?? string.Empty;
        }

        return DatasetRowMapper.Map(datasetId, row);
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
