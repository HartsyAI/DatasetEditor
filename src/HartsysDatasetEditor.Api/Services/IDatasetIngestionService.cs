using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Services;

internal interface IDatasetIngestionService
{
    Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default);
    Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default);
}
