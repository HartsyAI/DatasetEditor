using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>Repository interface for dataset CRUD operations with PostgreSQL</summary>
public interface IDatasetRepository
{
    /// <summary>Creates a new dataset and returns its ID</summary>
    Task<Guid> CreateAsync(DatasetDto dataset, CancellationToken cancellationToken = default);

    /// <summary>Gets a dataset by ID</summary>
    Task<DatasetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets all datasets with pagination</summary>
    Task<List<DatasetDto>> GetAllAsync(int page = 0, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing dataset</summary>
    Task UpdateAsync(DatasetDto dataset, CancellationToken cancellationToken = default);

    /// <summary>Deletes a dataset (metadata only, Parquet files handled separately)</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets total count of datasets</summary>
    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Searches datasets by name or description</summary>
    Task<List<DatasetDto>> SearchAsync(string query, int page = 0, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>Updates dataset status (e.g., during ingestion)</summary>
    Task UpdateStatusAsync(Guid id, IngestionStatusDto status, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>Updates item count for a dataset</summary>
    Task UpdateItemCountAsync(Guid id, long count, CancellationToken cancellationToken = default);
}
