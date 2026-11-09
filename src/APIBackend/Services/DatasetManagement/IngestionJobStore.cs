using System.Security.Cryptography;
using System.Text.Json;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// The PostgreSQL-backed ingestion queue (table <c>ingestion_jobs</c>). Replaces an in-memory
/// channel that lost every queued job on restart (audit I-1). A HuggingFace access token is
/// encrypted with Data Protection while the job waits and removed when the job finishes.
/// </summary>
public sealed class IngestionJobStore : IIngestionJobQueue, IIngestionJobStore
{
    /// <summary>Data Protection purpose for stored HuggingFace tokens.</summary>
    private const string TokenProtectionPurpose = "DatasetStudio.IngestionJobs.HuggingFaceToken";

    /// <summary>Postgres unique_violation: another worker won the one-running-job-per-dataset race.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>pg_advisory_xact_lock key serialising queue admission ("DSIQ").</summary>
    private const long AdmissionLockKey = 0x44534951;

    private readonly DatasetStudioDbContext _db;
    private readonly IDataProtector _tokenProtector;
    private readonly IngestionSignal _signal;
    private readonly IngestionOptions _options;

    /// <summary>Creates the store for one DI scope.</summary>
    public IngestionJobStore(
        DatasetStudioDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IngestionSignal signal,
        IOptions<IngestionOptions> options)
    {
        _db = db;
        _tokenProtector = dataProtectionProvider.CreateProtector(TokenProtectionPurpose);
        _signal = signal;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<Guid?> TryReserveAsync(IngestionJob job, CancellationToken cancellationToken)
    {
        return TryAdmitAsync(job, IngestionJobState.Staging, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CommitAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await _db.IngestionJobs
            .Where(j => j.Id == jobId && j.State == IngestionJobState.Staging)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, IngestionJobState.Queued), cancellationToken);
        _signal.Notify();
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await _db.IngestionJobs
            .Where(j => j.Id == jobId && j.State == IngestionJobState.Staging)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryEnqueueAsync(IngestionJob job, CancellationToken cancellationToken)
    {
        Guid? id = await TryAdmitAsync(job, IngestionJobState.Queued, cancellationToken);
        if (id is null)
        {
            return false;
        }

        _signal.Notify();
        return true;
    }

    /// <inheritdoc />
    public async Task DiscardQueuedJobsAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        List<string?> stagedFiles = await _db.IngestionJobs
            .Where(j => j.DatasetId == datasetId
                && (j.State == IngestionJobState.Queued || j.State == IngestionJobState.Staging))
            .Select(j => j.FilePath)
            .ToListAsync(cancellationToken);

        await _db.IngestionJobs
            .Where(j => j.DatasetId == datasetId
                && (j.State == IngestionJobState.Queued || j.State == IngestionJobState.Staging))
            .ExecuteDeleteAsync(cancellationToken);

        // FilePath is only set for uploads, which are staged copies this app owns; a folder
        // job's FolderPath is the caller's own folder and is never touched.
        foreach (string? path in stagedFiles)
        {
            DeleteStagedFile(path);
        }
    }

    /// <inheritdoc />
    public async Task<int> DiscardAbandonedReservationsAsync(CancellationToken cancellationToken)
    {
        List<string?> stagedFiles = await _db.IngestionJobs
            .Where(j => j.State == IngestionJobState.Staging)
            .Select(j => j.FilePath)
            .ToListAsync(cancellationToken);
        int removed = await _db.IngestionJobs
            .Where(j => j.State == IngestionJobState.Staging)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (string? path in stagedFiles)
        {
            DeleteStagedFile(path);
        }
        return removed;
    }

    /// <summary>
    /// Inserts the job if fewer than <see cref="IngestionOptions.MaxQueuedJobs"/> jobs are
    /// staging, queued or running. The count and the insert run under a transaction-scoped
    /// advisory lock, so concurrent requests (in any process) are admitted one at a time and
    /// can't all see the same free slot.
    /// </summary>
    private async Task<Guid?> TryAdmitAsync(IngestionJob job, IngestionJobState state, CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            IngestionJobEntity entity = new()
            {
                Id = Guid.NewGuid(),
                DatasetId = job.DatasetId,
                Kind = job.HuggingFaceRequest is not null ? IngestionJobKind.HuggingFace
                    : job.FolderPath is not null ? IngestionJobKind.Folder
                    : IngestionJobKind.Upload,
                State = state,
                FilePath = job.FilePath,
                FolderPath = job.FolderPath,
                PayloadJson = SerializeRequest(job.HuggingFaceRequest),
                CreatedAt = DateTime.UtcNow
            };

            await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdmissionLockKey})", cancellationToken);
                int active = await _db.IngestionJobs.CountAsync(
                    j => j.State == IngestionJobState.Queued
                        || j.State == IngestionJobState.Running
                        || j.State == IngestionJobState.Staging,
                    cancellationToken);
                if (active >= _options.MaxQueuedJobs)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (Guid?)null;
                }

                _db.IngestionJobs.Add(entity);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return entity.Id;
            }
            catch
            {
                // A retried attempt builds a new entity; don't leave this one tracked as Added.
                _db.Entry(entity).State = EntityState.Detached;
                throw;
            }
        });
    }

    private static void DeleteStagedFile(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Still open (an upload being written right now); the endpoint deletes it when its
            // commit finds the reservation gone.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public async Task<ClaimedIngestionJob?> TryClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        List<IngestionJobEntity> claimed;
        try
        {
            // One statement: pick the oldest queued job whose dataset is idle, lock it (skipping
            // rows another worker has locked), and mark it running.
            claimed = await _db.IngestionJobs
                .FromSqlInterpolated($"""
                    UPDATE ingestion_jobs
                    SET state = 1, attempts = attempts + 1, claimed_by = {workerId},
                        started_at = {now}, items_before = NULL
                    WHERE id = (
                        SELECT j.id FROM ingestion_jobs j
                        WHERE j.state = 0
                          AND NOT EXISTS (
                              SELECT 1 FROM ingestion_jobs r
                              WHERE r.dataset_id = j.dataset_id AND r.state = 1)
                        ORDER BY j.created_at
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1)
                    RETURNING *
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Another worker started a job for the same dataset in the same instant. Leave this
            // one queued; the next poll picks it up after that job finishes.
            return null;
        }

        return claimed.Count == 0 ? null : ToClaimed(claimed[0]);
    }

    /// <inheritdoc />
    public async Task SetItemsBeforeAsync(Guid jobId, long itemsBefore, CancellationToken cancellationToken)
    {
        await _db.IngestionJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ItemsBefore, itemsBefore), cancellationToken);
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return FinishAsync(jobId, IngestionJobState.Completed, error: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken)
    {
        return FinishAsync(jobId, IngestionJobState.Failed, error, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequeueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // created_at is kept, so the job goes back to its old place at the front of the queue.
        await _db.IngestionJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, IngestionJobState.Queued)
                .SetProperty(j => j.ClaimedBy, (string?)null)
                .SetProperty(j => j.StartedAt, (DateTime?)null)
                .SetProperty(j => j.ItemsBefore, (long?)null),
                cancellationToken);
        _signal.Notify();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClaimedIngestionJob>> GetInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        List<IngestionJobEntity> running = await _db.IngestionJobs
            .Where(j => j.State == IngestionJobState.Running)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
        return running.Select(ToClaimed).ToList();
    }

    /// <inheritdoc />
    public async Task<int> FailOrphanedProcessingDatasetsAsync(string message, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        return await _db.Datasets
            .Where(d => d.Status == IngestionStatusDto.Processing
                && !_db.IngestionJobs.Any(j => j.DatasetId == d.Id
                    && (j.State == IngestionJobState.Queued || j.State == IngestionJobState.Running)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, IngestionStatusDto.Failed)
                .SetProperty(d => d.ErrorMessage, message)
                .SetProperty(d => d.UpdatedAt, now),
                cancellationToken);
    }

    private async Task FinishAsync(Guid jobId, IngestionJobState state, string? error, CancellationToken cancellationToken)
    {
        IngestionJobEntity? entity = await _db.IngestionJobs
            .AsTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (entity is null)
        {
            // The dataset was deleted while its job ran; the cascade already removed the job.
            return;
        }

        entity.State = state;
        entity.Error = error;
        entity.FinishedAt = DateTime.UtcNow;
        entity.PayloadJson = StripToken(entity.PayloadJson);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private ClaimedIngestionJob ToClaimed(IngestionJobEntity entity)
    {
        ImportHuggingFaceDatasetRequest? request = null;
        string? payloadError = null;
        if (entity.Kind == IngestionJobKind.HuggingFace)
        {
            try
            {
                request = DeserializeRequest(entity.PayloadJson);
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidOperationException)
            {
                payloadError = "The queued import's stored access token could not be decrypted (the server's "
                    + "Data Protection keys changed). Start the import again.";
            }
        }

        IngestionJob job = new(entity.DatasetId, entity.FilePath, request, entity.FolderPath);
        return new ClaimedIngestionJob(entity.Id, entity.Attempts, entity.ItemsBefore, job, payloadError);
    }

    private string? SerializeRequest(ImportHuggingFaceDatasetRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        ImportHuggingFaceDatasetRequest stored = string.IsNullOrEmpty(request.AccessToken)
            ? request
            : request with { AccessToken = _tokenProtector.Protect(request.AccessToken) };
        return JsonSerializer.Serialize(stored);
    }

    private ImportHuggingFaceDatasetRequest DeserializeRequest(string? payloadJson)
    {
        ImportHuggingFaceDatasetRequest stored = JsonSerializer.Deserialize<ImportHuggingFaceDatasetRequest>(payloadJson ?? string.Empty)
            ?? throw new InvalidOperationException("Empty HuggingFace job payload.");
        return string.IsNullOrEmpty(stored.AccessToken)
            ? stored
            : stored with { AccessToken = _tokenProtector.Unprotect(stored.AccessToken) };
    }

    private static string? StripToken(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return payloadJson;
        }

        try
        {
            ImportHuggingFaceDatasetRequest? stored = JsonSerializer.Deserialize<ImportHuggingFaceDatasetRequest>(payloadJson);
            return stored is null ? null : JsonSerializer.Serialize(stored with { AccessToken = null });
        }
        catch (JsonException)
        {
            // Unreadable payload: drop it rather than keep something that may hold a token.
            return null;
        }
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation })
            {
                return true;
            }
        }
        return false;
    }
}
