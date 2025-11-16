using System.Text;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;

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

            List<DatasetItemDto> parsedItems = await ParseUnsplashTsvAsync(uploadLocation, cancellationToken);
            if (parsedItems.Count > 0)
            {
                await _datasetItemRepository.AddRangeAsync(datasetId, parsedItems, cancellationToken);
            }

            dataset.TotalItems = parsedItems.Count;
            dataset.Status = Contracts.Datasets.IngestionStatusDto.Completed;
            await _datasetRepository.UpdateAsync(dataset, cancellationToken);
            _logger.LogInformation("Ingestion completed for dataset {DatasetId} with {ItemCount} items", datasetId, parsedItems.Count);
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

    private async Task<List<DatasetItemDto>> ParseUnsplashTsvAsync(string filePath, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
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
            Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase)
            {
                ["photographer_username"] = GetValue(values, "photographer_username"),
                ["photo_url"] = GetValue(values, "photo_url"),
                ["photo_location_name"] = GetValue(values, "photo_location_name"),
                ["photo_location_latitude"] = GetValue(values, "photo_location_latitude"),
                ["photo_location_longitude"] = GetValue(values, "photo_location_longitude")
            };

            string title = GetValue(values, "photo_description");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Untitled photo";
            }

            DatasetItemDto dto = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = GetValue(values, "photo_id"),
                Title = title,
                Description = GetValue(values, "photo_description"),
                ImageUrl = imageUrl,
                ThumbnailUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : $"{imageUrl}?w=400&q=80",
                Metadata = metadata
            };

            items.Add(dto);
        }

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
}
