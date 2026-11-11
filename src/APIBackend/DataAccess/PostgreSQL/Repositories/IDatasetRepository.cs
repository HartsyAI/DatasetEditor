using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>Repository interface for dataset CRUD operations with PostgreSQL</summary>
public interface IDatasetRepository
{
    /// <summary>Creates a new dataset and returns its ID</summary>
    Task<Guid> CreateAsync(DatasetDto dataset, CancellationToken cancellationToken = default);

    /// <summary>Gets a dataset by ID</summary>
    Task<DatasetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets all datasets with pagination. Pass <paramref name="visibility"/> to restrict rows to what that principal may see (anonymous read requires IsPublic; null means unfiltered).</summary>
    Task<List<DatasetDto>> GetAllAsync(int page = 0, int pageSize = 50, DatasetVisibilityFilter? visibility = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the fields that differ between <paramref name="original"/> (the dataset as the
    /// caller read it) and <paramref name="updated"/>, and nothing else. It used to overwrite the
    /// whole row from a snapshot often held across long awaits, so it reverted status and
    /// item-count writes that background ingestion made in the meantime (audit D-4).
    /// </summary>
    /// <exception cref="DatasetUpdateConflictException">
    /// A field being changed now holds a value other than the original and the new one: another
    /// request changed it first.
    /// </exception>
    /// <exception cref="InvalidOperationException">The dataset doesn't exist.</exception>
    Task UpdateAsync(DatasetDto original, DatasetDto updated, CancellationToken cancellationToken = default);

    /// <summary>Deletes a dataset (metadata only, Parquet files handled separately)</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets total count of datasets. Must use the same <paramref name="visibility"/> as the matching GetAllAsync call or pagination totals won't match the rows actually returned.</summary>
    Task<long> GetCountAsync(DatasetVisibilityFilter? visibility = null, CancellationToken cancellationToken = default);


    /// <summary>Updates dataset status (e.g., during ingestion)</summary>
    Task UpdateStatusAsync(Guid id, IngestionStatusDto status, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>Updates item count for a dataset</summary>
    Task UpdateItemCountAsync(Guid id, long count, CancellationToken cancellationToken = default);
}
