namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Limits for background ingestion (configuration section <c>Ingestion</c>). The queue used to be
/// unbounded and drained by one consumer, so a slow HuggingFace download blocked every other
/// upload and staged files could fill the disk (audit I-3, I-4).
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>How many jobs run at once. Jobs for the same dataset never run together.</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>Queued plus running jobs allowed before new imports get 503.</summary>
    public int MaxQueuedJobs { get; set; } = 50;

    /// <summary>How many times an interrupted job that wrote nothing is retried after a restart.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// How often idle workers look for work without being signalled. Enqueues and requeues in
    /// this process wake a worker at once, and a worker that finishes a job looks again
    /// straight away, so this is only a safety net (for example, a job queued by another
    /// process). Kept long because every poll is a logged SQL statement.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
}
