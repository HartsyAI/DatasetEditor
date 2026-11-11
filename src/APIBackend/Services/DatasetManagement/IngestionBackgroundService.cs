using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Runs queued ingestion jobs from <c>ingestion_jobs</c> with
/// <see cref="IngestionOptions.MaxConcurrentJobs"/> workers. It used to drain an in-memory channel
/// with one consumer, so a restart lost every queued job and one slow import blocked all others
/// (audit I-1, I-3). At startup it first recovers jobs the previous process left running.
/// </summary>
public sealed class IngestionBackgroundService : BackgroundService
{
    /// <summary>Recorded on a dataset whose Processing state no job will ever finish.</summary>
    internal const string OrphanedDatasetMessage =
        "Ingestion was interrupted by a server restart and its job was lost. Start the import again.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionSignal _signal;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionBackgroundService> _logger;

    /// <summary>
    /// When this process's worker host was created, before the server accepts requests. Only
    /// upload reservations older than this are abandoned ones.
    /// </summary>
    private readonly DateTime _startedAt = DateTime.UtcNow;

    /// <summary>Creates the worker host.</summary>
    public IngestionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IngestionSignal signal,
        IOptions<IngestionOptions> options,
        ILogger<IngestionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Recovers interrupted jobs, then runs the workers until shutdown.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host startup finish before touching the database.
        await Task.Yield();

        try
        {
            await RecoverInterruptedJobsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Recovery failing must not stop new jobs from running; the next start retries it.
            _logger.LogError(ex, "Could not recover interrupted ingestion jobs");
        }

        int workerCount = Math.Max(1, _options.MaxConcurrentJobs);
        string instance = $"{Environment.MachineName}:{Environment.ProcessId}";
        IEnumerable<Task> workers = Enumerable.Range(0, workerCount)
            .Select(index => RunWorkerAsync($"{instance}:{index}", stoppingToken));
        await Task.WhenAll(workers);
    }

    /// <summary>
    /// Requeues each job the previous process left running if it wrote nothing (ingestion
    /// appends, so a job that already wrote items would duplicate them on a second run), and
    /// fails the rest. Then fails datasets stuck in Processing with no job at all.
    /// </summary>
    internal async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IIngestionJobStore store = scope.ServiceProvider.GetRequiredService<IIngestionJobStore>();
        IDatasetRepository datasets = scope.ServiceProvider.GetRequiredService<IDatasetRepository>();
        IDatasetItemRepository items = scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>();

        IReadOnlyList<ClaimedIngestionJob> interrupted = await store.GetInterruptedJobsAsync(cancellationToken);
        foreach (ClaimedIngestionJob claimed in interrupted)
        {
            Guid datasetId = claimed.Job.DatasetId;
            try
            {
                long itemsNow = await items.GetCountAsync(datasetId, cancellationToken);
                long written = claimed.ItemsBefore is long before ? itemsNow - before : 0;

                if (written == 0 && claimed.PayloadError is null && claimed.Attempts < _options.MaxAttempts)
                {
                    await store.RequeueAsync(claimed.Id, cancellationToken);
                    // The interrupted run may have reported progress for rows that never reached
                    // a shard; show the real count while the job waits to run again.
                    await datasets.UpdateItemCountAsync(datasetId, itemsNow, cancellationToken);
                    await datasets.UpdateStatusAsync(datasetId, IngestionStatusDto.Pending, null, cancellationToken);
                    _logger.LogWarning(
                        "Requeued ingestion job {JobId} for dataset {DatasetId}: interrupted by a restart before writing any items (attempt {Attempt} of {MaxAttempts})",
                        claimed.Id, datasetId, claimed.Attempts, _options.MaxAttempts);
                    continue;
                }

                string message = claimed.PayloadError
                    ?? (written > 0
                        ? $"Ingestion was interrupted by a server restart after writing {written} item(s). Those items were kept."
                        : $"Ingestion was interrupted by a server restart {claimed.Attempts} time(s) and was not retried again.");
                await store.FailAsync(claimed.Id, message, cancellationToken);
                await datasets.UpdateItemCountAsync(datasetId, itemsNow, cancellationToken);
                await datasets.UpdateStatusAsync(datasetId, IngestionStatusDto.Failed, message, cancellationToken);
                TryDeleteStagedFile(claimed.Job.FilePath);
                _logger.LogWarning("Failed interrupted ingestion job {JobId} for dataset {DatasetId}: {Message}", claimed.Id, datasetId, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Typically the item count can't be read (a shard cut short by the crash). Left
                // Running, the job would block every later job for this dataset forever.
                _logger.LogError(ex, "Could not recover ingestion job {JobId} for dataset {DatasetId}", claimed.Id, datasetId);
                await FailJobAsync(store, datasets, claimed,
                    $"Ingestion was interrupted by a server restart and could not be recovered: {ex.Message}");
            }
        }

        int abandoned = await store.DiscardAbandonedReservationsAsync(_startedAt, cancellationToken);
        if (abandoned > 0)
        {
            _logger.LogWarning("Removed {Count} upload reservation(s) whose upload never finished", abandoned);
        }

        int orphaned = await store.FailOrphanedProcessingDatasetsAsync(OrphanedDatasetMessage, cancellationToken);
        if (orphaned > 0)
        {
            _logger.LogWarning("Marked {Count} dataset(s) Failed: they were Processing with no ingestion job", orphaned);
        }
    }

    private async Task RunWorkerAsync(string workerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool ranJob;
            try
            {
                ranJob = await TryRunNextJobAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Usually the database being unreachable. Back off for one poll and try again.
                _logger.LogError(ex, "Ingestion worker {WorkerId} could not claim a job", workerId);
                ranJob = false;
            }

            if (ranJob)
            {
                continue;
            }

            try
            {
                await _signal.WaitAsync(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Claims and runs one job. Returns false when the queue had nothing to run.</summary>
    internal async Task<bool> TryRunNextJobAsync(string workerId, CancellationToken stoppingToken)
    {
        // Each job gets its own DI scope: the ingestion service and repositories are scoped.
        using IServiceScope scope = _scopeFactory.CreateScope();
        IIngestionJobStore store = scope.ServiceProvider.GetRequiredService<IIngestionJobStore>();

        ClaimedIngestionJob? claimed = await store.TryClaimNextAsync(workerId, stoppingToken);
        if (claimed is null)
        {
            return false;
        }

        await RunClaimedJobAsync(scope, store, claimed, stoppingToken);
        return true;
    }

    private async Task RunClaimedJobAsync(IServiceScope scope, IIngestionJobStore store, ClaimedIngestionJob claimed, CancellationToken stoppingToken)
    {
        IngestionJob job = claimed.Job;
        IDatasetRepository datasets = scope.ServiceProvider.GetRequiredService<IDatasetRepository>();

        if (claimed.PayloadError is not null)
        {
            await FailJobAsync(store, datasets, claimed, claimed.PayloadError);
            return;
        }

        try
        {
            IDatasetItemRepository items = scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>();
            long itemsBefore = await items.GetCountAsync(job.DatasetId, stoppingToken);
            await store.SetItemsBeforeAsync(claimed.Id, itemsBefore, stoppingToken);

            IDatasetIngestionService ingestion = scope.ServiceProvider.GetRequiredService<IDatasetIngestionService>();
            _logger.LogInformation("Starting ingestion job {JobId} for dataset {DatasetId}", claimed.Id, job.DatasetId);
            if (job.HuggingFaceRequest is not null)
            {
                await ingestion.ImportFromHuggingFaceAsync(job.DatasetId, job.HuggingFaceRequest, stoppingToken);
            }
            else if (job.FolderPath is not null)
            {
                await ingestion.IngestFromFolderAsync(job.DatasetId, job.FolderPath, stoppingToken);
            }
            else
            {
                await ingestion.StartIngestionAsync(job.DatasetId, job.FilePath, stoppingToken);
            }

            await store.CompleteAsync(claimed.Id, CancellationToken.None);
            TryDeleteStagedFile(job.FilePath);
            _logger.LogInformation("Completed ingestion job {JobId} for dataset {DatasetId}", claimed.Id, job.DatasetId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Left Running on purpose, with its staged file: the next start requeues it if it
            // wrote nothing, or fails it if it did.
            _logger.LogWarning("Ingestion job {JobId} for dataset {DatasetId} stopped by shutdown; it is recovered at the next start", claimed.Id, job.DatasetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion job {JobId} failed for dataset {DatasetId}", claimed.Id, job.DatasetId);
            await FailJobAsync(store, datasets, claimed, ex.Message);
        }
    }

    private async Task FailJobAsync(IIngestionJobStore store, IDatasetRepository datasets, ClaimedIngestionJob claimed, string message)
    {
        try
        {
            await store.FailAsync(claimed.Id, message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record failure of ingestion job {JobId}", claimed.Id);
        }

        try
        {
            // The ingestion service records its own failures with more detail; this covers the
            // ones it never saw (an undecryptable payload, a failure before it started).
            DatasetRecord? dataset = await datasets.GetByIdAsync(claimed.Job.DatasetId, CancellationToken.None);
            if (dataset is not null && dataset.Status != IngestionStatusDto.Failed)
            {
                await datasets.UpdateStatusAsync(claimed.Job.DatasetId, IngestionStatusDto.Failed, message, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record ingestion failure for dataset {DatasetId}", claimed.Job.DatasetId);
        }

        TryDeleteStagedFile(claimed.Job.FilePath);
    }

    private void TryDeleteStagedFile(string? path)
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
            _logger.LogWarning(ex, "Could not delete staged ingestion file {Path}", path);
        }
    }
}
