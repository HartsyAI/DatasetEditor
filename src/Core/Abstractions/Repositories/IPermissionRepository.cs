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

    /// <summary>
    /// Creates the (dataset, user) grant, or replaces the existing one's rights, in one statement.
    /// With <paramref name="onlyReplacingGrantsBy"/> set, an existing grant is replaced only if
    /// that user granted it; returns false when it belongs to someone else.
    /// </summary>
    Task<bool> UpsertAsync(PermissionDto grant, Guid? onlyReplacingGrantsBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the (dataset, user) grant, only if <paramref name="onlyIfGrantedBy"/> granted it
    /// when set. False when nothing matched.
    /// </summary>
    Task<bool> DeleteAsync(Guid datasetId, Guid userId, Guid? onlyIfGrantedBy, CancellationToken cancellationToken = default);
}
