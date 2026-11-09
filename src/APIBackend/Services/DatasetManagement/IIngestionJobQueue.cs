namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// What the import endpoints use to queue ingestion. Jobs are rows in <c>ingestion_jobs</c>, so a
/// restart no longer loses them (audit I-1), and the queue has a limit (audit I-4).
/// </summary>
/// <remarks>
/// Admission is atomic: the limit check and the insert happen under one database lock. A
/// separate "is there room?" check followed by an insert let concurrent uploads near the limit
/// all pass, each then copying a whole file to disk before queuing.
/// </remarks>
public interface IIngestionJobQueue
{
    /// <summary>
    /// Reserves a slot for a job whose input is still being prepared (an upload being written to
    /// disk, a dataset being updated). Returns the job id, or null when the queue is full. The
    /// reservation is never run until <see cref="CommitAsync"/>; call <see cref="ReleaseAsync"/>
    /// if preparing fails.
    /// </summary>
    Task<Guid?> TryReserveAsync(IngestionJob job, CancellationToken cancellationToken);

    /// <summary>Turns a reservation into a queued job and wakes a worker.</summary>
    Task CommitAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Drops a reservation whose input could not be prepared.</summary>
    Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Reserves and commits in one step. Returns false when the queue is full.</summary>
    Task<bool> TryEnqueueAsync(IngestionJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a dataset's queued and reserved jobs and deletes their staged upload files. Called
    /// before a dataset is deleted: the cascade would otherwise remove the only record of where
    /// those files are, and they would stay on the upload volume for good.
    /// </summary>
    Task DiscardQueuedJobsAsync(Guid datasetId, CancellationToken cancellationToken);
}
