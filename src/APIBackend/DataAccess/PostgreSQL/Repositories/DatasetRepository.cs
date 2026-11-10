using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.DTO.Datasets;
using Microsoft.EntityFrameworkCore;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// Entity Framework Core implementation of IDatasetRepository for PostgreSQL.
/// Handles mapping between DatasetEntity (DB) and DatasetDto (application).
/// </summary>
public sealed class DatasetRepository : IDatasetRepository
{
    private readonly DatasetStudioDbContext _dbContext;

    public DatasetRepository(DatasetStudioDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Guid> CreateAsync(DatasetDto dataset, CancellationToken cancellationToken = default)
    {
        if (dataset == null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        var entity = new DatasetEntity
        {
            Id = dataset.Id == Guid.Empty ? Guid.NewGuid() : dataset.Id,
            Name = dataset.Name,
            Description = dataset.Description,
            Status = dataset.Status,
            SourceFileName = dataset.SourceFileName,
            SourceType = dataset.SourceType,
            SourceUri = dataset.SourceUri,
            IsStreaming = dataset.IsStreaming,
            HuggingFaceRepository = dataset.HuggingFaceRepository,
            HuggingFaceConfig = dataset.HuggingFaceConfig,
            HuggingFaceSplit = dataset.HuggingFaceSplit,
            S3StorageProfileName = dataset.S3StorageProfileName,
            S3Bucket = dataset.S3Bucket,
            S3Prefix = dataset.S3Prefix,
            TotalItems = dataset.TotalItems,
            ErrorMessage = dataset.ErrorMessage,
            Format = dataset.Format,
            Modality = dataset.Modality,
            TotalSizeBytes = dataset.TotalSizeBytes,
            StoragePath = dataset.StoragePath,
            ParquetPath = dataset.ParquetPath,
            IsPublic = dataset.IsPublic,
            Metadata = dataset.Metadata,
            CreatedByUserId = dataset.CreatedByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Datasets.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task<DatasetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Datasets
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<List<DatasetDto>> GetAllAsync(int page = 0, int pageSize = 50, DatasetVisibilityFilter? visibility = null, CancellationToken cancellationToken = default)
    {
        var entities = await ApplyVisibilityFilter(_dbContext.Datasets.AsNoTracking(), visibility)
            .OrderByDescending(d => d.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    /// <summary>
    /// Translates to a real SQL WHERE clause (EF-translated), applied before Skip/Take -- filtering
    /// an already-fetched page in memory instead would silently under-fill pages whenever some of
    /// the fetched rows aren't visible to the caller (the same undercounting bug class Phase 1
    /// fixed for item-cursor filtering).
    /// </summary>
    private static IQueryable<DatasetEntity> ApplyVisibilityFilter(IQueryable<DatasetEntity> query, DatasetVisibilityFilter? visibility)
    {
        if (visibility is null || visibility.Value.IsAdmin)
        {
            return query; // unfiltered: internal/background caller, or an admin who can see everything
        }

        // Datasets shared with the caller (an unexpired grant) are listed too, the same set the
        // permission service lets them open (audit D-2).
        Guid? userId = visibility.Value.RequestingUserId;
        DateTime now = DateTime.UtcNow;
        return query.Where(d => d.IsPublic
            || (userId != null
                && (d.CreatedByUserId == userId
                    || d.Permissions.Any(p => p.UserId == userId && (p.ExpiresAt == null || p.ExpiresAt > now)))));
    }

    public async Task UpdateAsync(DatasetDto original, DatasetDto updated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        if (original.Id != updated.Id)
        {
            throw new ArgumentException("original and updated must be the same dataset.", nameof(updated));
        }

        // The row's xmin token makes SaveChanges fail if anything (typically a background status
        // or count write, or another patch) touched the row between this read and the write. Each
        // retry re-reads and re-checks, so those writes are kept, never reverted. Three attempts
        // weren't enough with five concurrent writers: a patch that conflicted with nothing failed
        // with a false 409. Retries back off with jitter so contending writers spread out.
        const int MaxAttempts = 10;
        for (int attempt = 1; ; attempt++)
        {
            // AsTracking(): the DbContext defaults to NoTracking, which would make this
            // fetch-mutate-SaveChanges pattern silently write nothing.
            DatasetEntity entity = await _dbContext.Datasets
                .AsTracking()
                .FirstOrDefaultAsync(d => d.Id == updated.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Dataset with ID {updated.Id} not found");

            FieldPatch patch = new();

            // Where the items come from (type, HuggingFace repo/config/split, S3 profile/bucket/
            // prefix, folder) is one selection. If the caller changes any part of it while another
            // request changed any part since the caller's read, the update is refused as a whole:
            // merging field by field could store a combination no request chose or validated, such
            // as an S3 bucket on a dataset another request switched to HuggingFace, or a new
            // repository with the old one's resolved split (Codex review on #21).
            (string Name, object? Original, object? Updated, object? Current)[] source =
            {
                (nameof(DatasetDto.SourceType), original.SourceType, updated.SourceType, entity.SourceType),
                (nameof(DatasetDto.SourceUri), original.SourceUri, updated.SourceUri, entity.SourceUri),
                (nameof(DatasetDto.IsStreaming), original.IsStreaming, updated.IsStreaming, entity.IsStreaming),
                (nameof(DatasetDto.HuggingFaceRepository), original.HuggingFaceRepository, updated.HuggingFaceRepository, entity.HuggingFaceRepository),
                (nameof(DatasetDto.HuggingFaceConfig), original.HuggingFaceConfig, updated.HuggingFaceConfig, entity.HuggingFaceConfig),
                (nameof(DatasetDto.HuggingFaceSplit), original.HuggingFaceSplit, updated.HuggingFaceSplit, entity.HuggingFaceSplit),
                (nameof(DatasetDto.S3StorageProfileName), original.S3StorageProfileName, updated.S3StorageProfileName, entity.S3StorageProfileName),
                (nameof(DatasetDto.S3Bucket), original.S3Bucket, updated.S3Bucket, entity.S3Bucket),
                (nameof(DatasetDto.S3Prefix), original.S3Prefix, updated.S3Prefix, entity.S3Prefix),
                (nameof(DatasetDto.StoragePath), original.StoragePath, updated.StoragePath, entity.StoragePath),
            };
            if (source.Any(f => !Equals(f.Original, f.Updated)))
            {
                // Another request setting a field to exactly the caller's value (two reads resolving the
                // same split) agrees with the caller, so it isn't a conflict.
                List<string> changedElsewhere = source
                    .Where(f => !Equals(f.Current, f.Original) && !Equals(f.Current, f.Updated))
                    .Select(f => f.Name)
                    .ToList();
                if (changedElsewhere.Count > 0)
                {
                    _dbContext.Entry(entity).State = EntityState.Detached;
                    throw new DatasetUpdateConflictException(updated.Id, changedElsewhere);
                }
            }

            patch.Apply(nameof(DatasetDto.Name), original.Name, updated.Name, entity.Name, v => entity.Name = v);
            patch.Apply(nameof(DatasetDto.Description), original.Description, updated.Description, entity.Description, v => entity.Description = v);
            patch.Apply(nameof(DatasetDto.Status), original.Status, updated.Status, entity.Status, v => entity.Status = v);
            patch.Apply(nameof(DatasetDto.SourceFileName), original.SourceFileName, updated.SourceFileName, entity.SourceFileName, v => entity.SourceFileName = v);
            patch.Apply(nameof(DatasetDto.SourceType), original.SourceType, updated.SourceType, entity.SourceType, v => entity.SourceType = v);
            patch.Apply(nameof(DatasetDto.SourceUri), original.SourceUri, updated.SourceUri, entity.SourceUri, v => entity.SourceUri = v);
            patch.Apply(nameof(DatasetDto.IsStreaming), original.IsStreaming, updated.IsStreaming, entity.IsStreaming, v => entity.IsStreaming = v);
            patch.Apply(nameof(DatasetDto.HuggingFaceRepository), original.HuggingFaceRepository, updated.HuggingFaceRepository, entity.HuggingFaceRepository, v => entity.HuggingFaceRepository = v);
            patch.Apply(nameof(DatasetDto.HuggingFaceConfig), original.HuggingFaceConfig, updated.HuggingFaceConfig, entity.HuggingFaceConfig, v => entity.HuggingFaceConfig = v);
            patch.Apply(nameof(DatasetDto.HuggingFaceSplit), original.HuggingFaceSplit, updated.HuggingFaceSplit, entity.HuggingFaceSplit, v => entity.HuggingFaceSplit = v);
            patch.Apply(nameof(DatasetDto.S3StorageProfileName), original.S3StorageProfileName, updated.S3StorageProfileName, entity.S3StorageProfileName, v => entity.S3StorageProfileName = v);
            patch.Apply(nameof(DatasetDto.S3Bucket), original.S3Bucket, updated.S3Bucket, entity.S3Bucket, v => entity.S3Bucket = v);
            patch.Apply(nameof(DatasetDto.S3Prefix), original.S3Prefix, updated.S3Prefix, entity.S3Prefix, v => entity.S3Prefix = v);
            patch.Apply(nameof(DatasetDto.TotalItems), original.TotalItems, updated.TotalItems, entity.TotalItems, v => entity.TotalItems = v);
            patch.Apply(nameof(DatasetDto.ErrorMessage), original.ErrorMessage, updated.ErrorMessage, entity.ErrorMessage, v => entity.ErrorMessage = v);
            patch.Apply(nameof(DatasetDto.Format), original.Format, updated.Format, entity.Format, v => entity.Format = v);
            patch.Apply(nameof(DatasetDto.Modality), original.Modality, updated.Modality, entity.Modality, v => entity.Modality = v);
            patch.Apply(nameof(DatasetDto.TotalSizeBytes), original.TotalSizeBytes, updated.TotalSizeBytes, entity.TotalSizeBytes, v => entity.TotalSizeBytes = v);
            patch.Apply(nameof(DatasetDto.StoragePath), original.StoragePath, updated.StoragePath, entity.StoragePath, v => entity.StoragePath = v);
            patch.Apply(nameof(DatasetDto.ParquetPath), original.ParquetPath, updated.ParquetPath, entity.ParquetPath, v => entity.ParquetPath = v);
            patch.Apply(nameof(DatasetDto.IsPublic), original.IsPublic, updated.IsPublic, entity.IsPublic, v => entity.IsPublic = v);
            patch.Apply(nameof(DatasetDto.Metadata), original.Metadata, updated.Metadata, entity.Metadata, v => entity.Metadata = v);
            patch.Apply(nameof(DatasetDto.CreatedByUserId), original.CreatedByUserId, updated.CreatedByUserId, entity.CreatedByUserId, v => entity.CreatedByUserId = v);

            if (patch.Conflicts.Count > 0 || !patch.Changed)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                if (patch.Conflicts.Count > 0)
                {
                    throw new DatasetUpdateConflictException(updated.Id, patch.Conflicts);
                }
                return;
            }

            entity.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                if (attempt >= MaxAttempts)
                {
                    throw new DatasetUpdateConflictException(updated.Id, patch.ChangedFields);
                }
                await Task.Delay(Random.Shared.Next(5, 20) * attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Applies one field of a patch: unchanged by the caller → skipped; changed by the caller and
    /// still as the caller read it (or already at the new value) → written; changed by someone
    /// else to a third value → a conflict.
    /// </summary>
    private sealed class FieldPatch
    {
        public List<string> Conflicts { get; } = new();

        public List<string> ChangedFields { get; } = new();

        public bool Changed => ChangedFields.Count > 0;

        /// <param name="field">Field name, for conflict reports.</param>
        /// <param name="original">The value the caller read.</param>
        /// <param name="updated">The value the caller wants.</param>
        /// <param name="current">The value in the database now.</param>
        /// <param name="set">Writes the value to the tracked entity.</param>
        public void Apply<T>(string field, T original, T updated, T current, Action<T> set)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (comparer.Equals(original, updated) || comparer.Equals(current, updated))
            {
                return;
            }
            if (!comparer.Equals(current, original))
            {
                Conflicts.Add(field);
                return;
            }
            set(updated);
            ChangedFields.Add(field);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // AsTracking() here too: Remove() attaches its argument, and attaching a second,
        // separately-fetched (untracked) instance for a key the context is already tracking
        // an instance of throws "cannot be tracked because another instance ... is already
        // being tracked." A tracking query instead resolves to the existing tracked instance
        // by identity when there is one, so Remove() has nothing to conflict with.
        // Retried because the row's xmin token makes the delete fail if a background status or
        // count write landed between the read and the delete; the delete should still happen.
        for (int attempt = 1; ; attempt++)
        {
            var entity = await _dbContext.Datasets
                .AsTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (entity == null)
            {
                return; // Idempotent delete
            }

            _dbContext.Datasets.Remove(entity);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 10)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                await Task.Delay(Random.Shared.Next(5, 20) * attempt, cancellationToken);
            }
        }
    }

    public async Task<long> GetCountAsync(DatasetVisibilityFilter? visibility = null, CancellationToken cancellationToken = default)
    {
        return await ApplyVisibilityFilter(_dbContext.Datasets.AsNoTracking(), visibility).LongCountAsync(cancellationToken);
    }

    public async Task<List<DatasetDto>> SearchAsync(string query, int page = 0, int pageSize = 50, DatasetVisibilityFilter? visibility = null, CancellationToken cancellationToken = default)
    {
        var searchLower = query.ToLowerInvariant();

        var entities = await ApplyVisibilityFilter(_dbContext.Datasets.AsNoTracking(), visibility)
            .Where(d =>
                d.Name.ToLower().Contains(searchLower) ||
                (d.Description != null && d.Description.ToLower().Contains(searchLower)) ||
                (d.HuggingFaceRepository != null && d.HuggingFaceRepository.ToLower().Contains(searchLower))
            )
            .OrderByDescending(d => d.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    /// <summary>
    /// One UPDATE of just these columns, with no read first, so it can't revert another
    /// writer's fields and never trips the row's concurrency token (audit D-4).
    /// </summary>
    public async Task UpdateStatusAsync(Guid id, IngestionStatusDto status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        int updated = await _dbContext.Datasets
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.ErrorMessage, errorMessage)
                .SetProperty(d => d.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException($"Dataset with ID {id} not found");
        }
    }

    /// <summary>Same single-statement patch as <see cref="UpdateStatusAsync"/>, for the item count.</summary>
    public async Task UpdateItemCountAsync(Guid id, long count, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        int updated = await _dbContext.Datasets
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.TotalItems, count)
                .SetProperty(d => d.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException($"Dataset with ID {id} not found");
        }
    }

    /// <summary>
    /// Maps DatasetEntity to DatasetDto
    /// </summary>
    private static DatasetDto MapToDto(DatasetEntity entity)
    {
        return new DatasetDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Status = entity.Status,
            TotalItems = entity.TotalItems,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            SourceFileName = entity.SourceFileName,
            SourceType = entity.SourceType,
            SourceUri = entity.SourceUri,
            IsStreaming = entity.IsStreaming,
            HuggingFaceRepository = entity.HuggingFaceRepository,
            HuggingFaceConfig = entity.HuggingFaceConfig,
            HuggingFaceSplit = entity.HuggingFaceSplit,
            S3StorageProfileName = entity.S3StorageProfileName,
            S3Bucket = entity.S3Bucket,
            S3Prefix = entity.S3Prefix,
            ErrorMessage = entity.ErrorMessage,
            Format = entity.Format,
            Modality = entity.Modality,
            TotalSizeBytes = entity.TotalSizeBytes,
            StoragePath = entity.StoragePath,
            ParquetPath = entity.ParquetPath,
            IsPublic = entity.IsPublic,
            Metadata = entity.Metadata,
            CreatedByUserId = entity.CreatedByUserId
        };
    }
}
