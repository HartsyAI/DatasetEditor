using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Drains the <see cref="IngestionQueue"/>, running each ingestion off the request thread
/// and honoring application shutdown. Failures are recorded on the dataset so the client's
/// status poller surfaces them instead of the dataset being stuck "Pending"/"Processing".
/// </summary>
public sealed class IngestionBackgroundService : BackgroundService
{
    private readonly IngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionBackgroundService> _logger;

    public IngestionBackgroundService(
        IngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (IngestionJob job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(IngestionJob job, CancellationToken stoppingToken)
    {
        // Each job gets its own DI scope (the ingestion service + repositories are scoped).
        using IServiceScope scope = _scopeFactory.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<IDatasetIngestionService>();

        try
        {
            _logger.LogInformation("Starting background ingestion for dataset {DatasetId}", job.DatasetId);
            await ingestionService.StartIngestionAsync(job.DatasetId, job.FilePath, stoppingToken);
            _logger.LogInformation("Completed background ingestion for dataset {DatasetId}", job.DatasetId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Ingestion for dataset {DatasetId} cancelled by shutdown", job.DatasetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background ingestion failed for dataset {DatasetId}", job.DatasetId);
            await TrySetFailedAsync(scope, job.DatasetId, ex.Message);
        }
        finally
        {
            TryDeleteTempFile(job.FilePath);
        }
    }

    private async Task TrySetFailedAsync(IServiceScope scope, Guid datasetId, string message)
    {
        try
        {
            var datasetRepository = scope.ServiceProvider
                .GetRequiredService<Core.Abstractions.Repositories.IDatasetRepository>();
            await datasetRepository.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record ingestion failure for dataset {DatasetId}", datasetId);
        }
    }

    private void TryDeleteTempFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp ingestion file {Path}", path);
        }
    }
}
