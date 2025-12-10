using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

internal interface IDatasetIngestionService
{
    Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default);
    Task ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default);
}

