using System.Threading.Channels;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// A queued ingestion job: a previously-uploaded file to parse (<see cref="FilePath"/>), a
/// HuggingFace import request (<see cref="HuggingFaceRequest"/>), or a local folder to scan
/// (<see cref="FolderPath"/>) — exactly one is set. All three go through the same
/// queue/background-worker path so every ingestion source shares one entry point that owns
/// its own DI scope, rather than each endpoint managing its own fire-and-forget background
/// work (a HuggingFace import used to do that directly with Task.Run against a scoped
/// service, racing the request's DI scope being disposed).
/// </summary>
/// <param name="DatasetId">Target dataset.</param>
/// <param name="FilePath">Path to the uploaded temp file to ingest, deleted after processing.</param>
/// <param name="HuggingFaceRequest">HuggingFace import parameters, when this job is an HF import.</param>
/// <param name="FolderPath">Local folder to scan for images, when this job is a folder import — NOT deleted after processing (it's the caller's own folder, not a temp upload).</param>
public sealed record IngestionJob(
    Guid DatasetId,
    string? FilePath,
    ImportHuggingFaceDatasetRequest? HuggingFaceRequest = null,
    string? FolderPath = null);

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
