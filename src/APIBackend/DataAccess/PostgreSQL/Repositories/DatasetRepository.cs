using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.Core.Abstractions.Repositories;
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

        Guid? userId = visibility.Value.RequestingUserId;
        return query.Where(d => d.IsPublic || (userId != null && d.CreatedByUserId == userId));
    }

    public async Task UpdateAsync(DatasetDto dataset, CancellationToken cancellationToken = default)
    {
        if (dataset == null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        // AsTracking(): the DbContext is configured with a NoTracking default (see
        // ServiceCollectionExtensions — a deliberate read-performance win for the
        // GetAllAsync/GetByIdAsync/SearchAsync paths), which silently no-ops this
        // fetch-mutate-SaveChanges pattern without it — the entity comes back untracked, the
        // property assignments below are invisible to the change tracker, and SaveChangesAsync
        // has nothing to persist. Confirmed this was happening in practice: ingestion status
        // updates were never reaching the database at all.
        var entity = await _dbContext.Datasets
            .AsTracking()
            .FirstOrDefaultAsync(d => d.Id == dataset.Id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"Dataset with ID {dataset.Id} not found");
        }

        // Update fields
        entity.Name = dataset.Name;
        entity.Description = dataset.Description;
        entity.Status = dataset.Status;
        entity.SourceFileName = dataset.SourceFileName;
        entity.SourceType = dataset.SourceType;
        entity.SourceUri = dataset.SourceUri;
        entity.IsStreaming = dataset.IsStreaming;
        entity.HuggingFaceRepository = dataset.HuggingFaceRepository;
        entity.HuggingFaceConfig = dataset.HuggingFaceConfig;
        entity.HuggingFaceSplit = dataset.HuggingFaceSplit;
        entity.TotalItems = dataset.TotalItems;
        entity.ErrorMessage = dataset.ErrorMessage;
        entity.Format = dataset.Format;
        entity.Modality = dataset.Modality;
        entity.TotalSizeBytes = dataset.TotalSizeBytes;
        entity.StoragePath = dataset.StoragePath;
        entity.ParquetPath = dataset.ParquetPath;
        entity.IsPublic = dataset.IsPublic;
        entity.Metadata = dataset.Metadata;
        entity.CreatedByUserId = dataset.CreatedByUserId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // AsTracking() here too: Remove() attaches its argument, and attaching a second,
        // separately-fetched (untracked) instance for a key the context is already tracking
        // an instance of throws "cannot be tracked because another instance ... is already
        // being tracked." A tracking query instead resolves to the existing tracked instance
        // by identity when there is one, so Remove() has nothing to conflict with.
        var entity = await _dbContext.Datasets
            .AsTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity == null)
        {
            return; // Idempotent delete
        }

        _dbContext.Datasets.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task UpdateStatusAsync(Guid id, IngestionStatusDto status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        // See the AsTracking() comment in UpdateAsync — same fetch-mutate-SaveChanges pattern,
        // same requirement to opt out of the DbContext's NoTracking default.
        var entity = await _dbContext.Datasets
            .AsTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"Dataset with ID {id} not found");
        }

        entity.Status = status;
        entity.ErrorMessage = errorMessage;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateItemCountAsync(Guid id, long count, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Datasets
            .AsTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"Dataset with ID {id} not found");
        }

        entity.TotalItems = count;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
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
