namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// What the import endpoints use to queue ingestion. Jobs are rows in <c>ingestion_jobs</c>, so a
/// restart no longer loses them (audit I-1), and the queue has a limit (audit I-4).
/// </summary>
public interface IIngestionJobQueue
{
    /// <summary>
    /// Whether another job may be queued. Endpoints check this before staging an upload, so a
    /// full queue refuses the import with 503 instead of filling the disk with files nobody will
    /// process soon.
    /// </summary>
    Task<bool> HasCapacityAsync(CancellationToken cancellationToken);

    /// <summary>Stores the job and wakes a worker.</summary>
    Task EnqueueAsync(IngestionJob job, CancellationToken cancellationToken);
}
