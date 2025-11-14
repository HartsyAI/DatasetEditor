using HartsysDatasetEditor.Api.Models;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// Placeholder ingestion service. Updates dataset status without processing.
/// TODO: Replace with real ingestion pipeline (see docs/architecture.md section 3.3).
/// </summary>
internal sealed class NoOpDatasetIngestionService : IDatasetIngestionService
{
    private readonly IDatasetRepository _datasetRepository;

    public NoOpDatasetIngestionService(IDatasetRepository datasetRepository)
    {
        _datasetRepository = datasetRepository;
    }

    public async Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default)
    {
        var dataset = await _datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return;
        }

        dataset.Status = Contracts.Datasets.IngestionStatusDto.Completed;
        dataset.TotalItems = 0;
        await _datasetRepository.UpdateAsync(dataset, cancellationToken);
    }
}
