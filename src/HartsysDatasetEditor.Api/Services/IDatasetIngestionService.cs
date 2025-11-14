namespace HartsysDatasetEditor.Api.Services;

internal interface IDatasetIngestionService
{
    Task StartIngestionAsync(Guid datasetId, string? uploadLocation, CancellationToken cancellationToken = default);
}
