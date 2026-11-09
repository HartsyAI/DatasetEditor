namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>The worker's side of the persistent ingestion queue.</summary>
public interface IIngestionJobStore
{
    /// <summary>
    /// Claims the oldest queued job whose dataset has no running job, or returns null when there
    /// is none. Safe to call from several workers and processes at once.
    /// </summary>
    /// <param name="workerId">Recorded on the job so logs and the table show who ran it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClaimedIngestionJob?> TryClaimNextAsync(string workerId, CancellationToken cancellationToken);

    /// <summary>Records the dataset's item count as this attempt starts (see <see cref="ClaimedIngestionJob.ItemsBefore"/>).</summary>
    Task SetItemsBeforeAsync(Guid jobId, long itemsBefore, CancellationToken cancellationToken);

    /// <summary>Marks the job completed and drops any stored access token.</summary>
    Task CompleteAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Marks the job failed for good and drops any stored access token.</summary>
    Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken);

    /// <summary>Puts an interrupted job back in the queue.</summary>
    Task RequeueAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Jobs left running by a process that stopped. Called at startup, before any worker of this
    /// process claims anything, so every running job belongs to a previous process. This assumes
    /// one API instance per database, which is how Dataset Studio is deployed.
    /// </summary>
    Task<IReadOnlyList<ClaimedIngestionJob>> GetInterruptedJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fails every dataset still marked Processing that has no queued or running job: its work
    /// was lost (for example, queued in memory before jobs were persisted) and nothing will ever
    /// finish it. Returns how many datasets were failed.
    /// </summary>
    Task<int> FailOrphanedProcessingDatasetsAsync(string message, CancellationToken cancellationToken);
}
