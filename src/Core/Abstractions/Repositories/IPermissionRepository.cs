using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>Repository interface for per-dataset, per-user permission grants (PostgreSQL-backed).</summary>
public interface IPermissionRepository
{
    /// <summary>The permission grant for this exact (dataset, user) pair, if one exists.</summary>
    Task<PermissionDto?> GetAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Every grant on a dataset, expired ones included, with the users' names.</summary>
    Task<List<DatasetShareDto>> ListSharesAsync(Guid datasetId, CancellationToken cancellationToken = default);

    /// <summary>Creates the (dataset, user) grant, or replaces the existing one's rights.</summary>
    Task UpsertAsync(PermissionDto grant, CancellationToken cancellationToken = default);

    /// <summary>Removes the (dataset, user) grant. False when there was none.</summary>
    Task<bool> DeleteAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default);
}
