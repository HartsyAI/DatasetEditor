namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Wakes an idle ingestion worker when a job is queued in this process, so a new upload starts
/// immediately instead of waiting for the next poll. Losing a signal is harmless: the job is in
/// the database and the poll finds it.
/// </summary>
public sealed class IngestionSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);

    /// <summary>Wakes one waiting worker (or the next one to wait).</summary>
    public void Notify()
    {
        _semaphore.Release();
    }

    /// <summary>Waits until <see cref="Notify"/> is called or <paramref name="timeout"/> passes.</summary>
    /// <param name="timeout">The poll interval.</param>
    /// <param name="cancellationToken">Stops the wait on shutdown.</param>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(timeout, cancellationToken);
    }
}
