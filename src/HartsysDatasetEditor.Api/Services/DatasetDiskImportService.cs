using System.Text.Json;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HartsysDatasetEditor.Api.Services;

internal sealed class DatasetDiskImportService : IHostedService
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetIngestionService _ingestionService;
    private readonly IConfiguration _configuration;
    private readonly string _datasetRootPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DatasetDiskImportService(
        IDatasetRepository datasetRepository,
        IDatasetIngestionService ingestionService,
        IConfiguration configuration)
    {
        _datasetRepository = datasetRepository ?? throw new ArgumentNullException(nameof(datasetRepository));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _datasetRootPath = _configuration["Storage:DatasetRootPath"] ?? "./data/datasets";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => ScanAndImportAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ScanAndImportAsync(CancellationToken cancellationToken)
    {
        try
        {
            string root = Path.GetFullPath(_datasetRootPath);
            Directory.CreateDirectory(root);

            Logs.Info($"[DiskImport] Scanning dataset root: {root}");

            // Load existing datasets to avoid duplicates for disk-based imports
            IReadOnlyList<DatasetEntity> existingDatasets = await _datasetRepository.ListAsync(cancellationToken);
            HashSet<string> existingDiskSources = existingDatasets
                .Where(d => !string.IsNullOrWhiteSpace(d.SourceUri) && d.SourceUri!.StartsWith("disk:", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.SourceUri!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await ImportFromExistingDatasetFoldersAsync(root, cancellationToken);
            await ImportFromLooseFilesAsync(root, existingDiskSources, cancellationToken);
        }
        catch (Exception ex)
        {
            Logs.Warning($"[DiskImport] Failed during disk scan: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ImportFromExistingDatasetFoldersAsync(string root, CancellationToken cancellationToken)
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root);
        }
        catch (Exception ex)
        {
            Logs.Warning($"[DiskImport] Failed to enumerate dataset folders: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string metadataPath = Path.Combine(folder, "dataset.json");
            if (!File.Exists(metadataPath))
            {
                await TryAutoImportFolderWithoutMetadataAsync(folder, cancellationToken);
                continue;
            }

            DatasetDiskMetadata? metadata = null;
            try
            {
                string json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                metadata = JsonSerializer.Deserialize<DatasetDiskMetadata>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Logs.Warning($"[DiskImport] Failed to read metadata from {metadataPath}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (metadata == null)
            {
                continue;
            }

            Guid datasetId = metadata.Id != Guid.Empty ? metadata.Id : Guid.NewGuid();

            DatasetEntity? existing = await _datasetRepository.GetAsync(datasetId, cancellationToken);
            if (existing != null)
            {
                continue;
            }

            string folderName = Path.GetFileName(folder);

            DatasetEntity entity = new()
            {
                Id = datasetId,
                Name = string.IsNullOrWhiteSpace(metadata.Name) ? folderName : metadata.Name,
                Description = metadata.Description ?? $"Imported from disk folder '{folderName}'",
                Status = IngestionStatusDto.Pending,
                SourceFileName = metadata.SourceFileName ?? metadata.PrimaryFile,
                SourceType = metadata.SourceType,
                SourceUri = metadata.SourceUri,
                IsStreaming = false
            };

            await _datasetRepository.CreateAsync(entity, cancellationToken);

            // Ensure future restarts reuse the same dataset ID
            if (metadata.Id != datasetId)
            {
                metadata.Id = datasetId;
                try
                {
                    string updatedJson = JsonSerializer.Serialize(metadata, JsonOptions);
                    await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logs.Warning($"[DiskImport] Failed to update metadata ID in {metadataPath}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            string? primaryFile = metadata.PrimaryFile;
            if (string.IsNullOrWhiteSpace(primaryFile))
            {
                primaryFile = GuessPrimaryFile(folder);
            }

            if (!string.IsNullOrWhiteSpace(primaryFile))
            {
                string primaryPath = Path.Combine(folder, primaryFile);
                if (File.Exists(primaryPath))
                {
                    Logs.Info($"[DiskImport] Ingesting dataset {datasetId} from {primaryPath}");
                    await _ingestionService.StartIngestionAsync(datasetId, primaryPath, cancellationToken);
                }
            }
        }
    }

    private async Task ImportFromLooseFilesAsync(string root, HashSet<string> existingDiskSources, CancellationToken cancellationToken)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(root, "*.*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            Logs.Warning($"[DiskImport] Failed to enumerate loose files: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        string[] allowedExtensions = [".zip", ".tsv", ".tsv000", ".csv", ".csv000", ".parquet"]; 

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string ext = Path.GetExtension(file);
            if (!allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, file);
            string sourceUri = $"disk:{relative.Replace('\\', '/')}";
            if (existingDiskSources.Contains(sourceUri))
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(file);
            string fileName = Path.GetFileName(file);

            DatasetEntity entity = new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = $"Imported from disk file '{fileName}'",
                Status = IngestionStatusDto.Pending,
                SourceFileName = fileName,
                SourceType = DatasetSourceType.LocalUpload,
                SourceUri = sourceUri,
                IsStreaming = false
            };

            await _datasetRepository.CreateAsync(entity, cancellationToken);

            Logs.Info($"[DiskImport] Created dataset {entity.Id} from disk file {file}");
            await _ingestionService.StartIngestionAsync(entity.Id, file, cancellationToken);
        }
    }

    private async Task TryAutoImportFolderWithoutMetadataAsync(string folder, CancellationToken cancellationToken)
    {
        string? primaryFile = GuessPrimaryFile(folder);
        if (string.IsNullOrWhiteSpace(primaryFile))
        {
            return;
        }

        string folderName = Path.GetFileName(folder);
        string primaryPath = Path.Combine(folder, primaryFile);
        if (!File.Exists(primaryPath))
        {
            return;
        }

        DatasetEntity entity = new()
        {
            Id = Guid.NewGuid(),
            Name = folderName,
            Description = $"Imported from disk folder '{folderName}'",
            Status = IngestionStatusDto.Pending,
            SourceFileName = primaryFile,
            SourceType = DatasetSourceType.LocalUpload,
            SourceUri = null,
            IsStreaming = false
        };

        await _datasetRepository.CreateAsync(entity, cancellationToken);

        DatasetDiskMetadata metadata = new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            SourceType = entity.SourceType,
            SourceUri = entity.SourceUri,
            SourceFileName = entity.SourceFileName,
            PrimaryFile = primaryFile,
            AuxiliaryFiles = new List<string>()
        };

        string metadataPath = Path.Combine(folder, "dataset.json");
        try
        {
            string json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Logs.Warning($"[DiskImport] Failed to write metadata for folder {folder}: {ex.GetType().Name}: {ex.Message}");
        }

        Logs.Info($"[DiskImport] Ingesting dataset {entity.Id} from folder {folder} using primary file {primaryFile}");
        await _ingestionService.StartIngestionAsync(entity.Id, primaryPath, cancellationToken);
    }

    private static string? GuessPrimaryFile(string folder)
    {
        string[] candidates =
        [
            "*.parquet",
            "*.tsv000",
            "*.csv000",
            "*.tsv",
            "*.csv",
            "*.zip"
        ];

        foreach (string pattern in candidates)
        {
            string[] files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                return Path.GetFileName(files[0]);
            }
        }

        return null;
    }
}
