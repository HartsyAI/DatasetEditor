using DatasetStudio.DTO.Auth;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>Repository interface for per-dataset, per-user permission grants (PostgreSQL-backed).</summary>
public interface IPermissionRepository
{
    /// <summary>The permission grant for this exact (dataset, user) pair, if one exists.</summary>
    Task<PermissionDto?> GetAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default);
}
