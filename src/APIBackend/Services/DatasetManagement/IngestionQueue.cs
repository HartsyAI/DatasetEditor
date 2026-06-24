using System.Threading.Channels;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>A queued ingestion job: a previously-uploaded file to parse into a dataset.</summary>
/// <param name="DatasetId">Target dataset.</param>
/// <param name="FilePath">Path to the uploaded temp file to ingest.</param>
public sealed record IngestionJob(Guid DatasetId, string FilePath);

/// <summary>
/// In-process queue for background ingestion. Upload endpoints enqueue work and return
/// immediately (202) instead of blocking the request thread on a multi-GB parse; the
/// <see cref="IngestionBackgroundService"/> drains the queue.
/// </summary>
public sealed class IngestionQueue
{
    private readonly Channel<IngestionJob> _channel =
        Channel.CreateUnbounded<IngestionJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>Enqueues a job. Returns false only if the queue has been completed.</summary>
    public bool Enqueue(IngestionJob job) => _channel.Writer.TryWrite(job);

    /// <summary>Reads queued jobs for the background worker.</summary>
    public ChannelReader<IngestionJob> Reader => _channel.Reader;
}
