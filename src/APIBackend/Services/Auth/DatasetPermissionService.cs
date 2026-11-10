using System.Security.Claims;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.Auth;

public sealed class DatasetPermissionService : IDatasetPermissionService
{
    private readonly IPermissionRepository _permissionRepository;

    public DatasetPermissionService(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository ?? throw new ArgumentNullException(nameof(permissionRepository));
    }

    public async Task<bool> CanReadAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        if (dataset.IsPublic)
        {
            return true;
        }
        return (await GetAccessAsync(dataset, user, cancellationToken)).CanRead;
    }

    public async Task<bool> CanWriteAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        (await GetAccessAsync(dataset, user, cancellationToken)).CanWrite;

    public async Task<bool> CanDeleteAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        (await GetAccessAsync(dataset, user, cancellationToken)).CanDelete;

    public async Task<DatasetAccess> GetAccessAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        Guid? userId = user.GetUserId();
        if (userId is null)
        {
            // Not authenticated: private-dataset access always requires a real account.
            return dataset.IsPublic ? DatasetAccess.None with { CanRead = true } : DatasetAccess.None;
        }

        if (user.IsSiteAdmin() || dataset.CreatedByUserId == userId)
        {
            return DatasetAccess.Full;
        }

        PermissionDto? grant = await _permissionRepository.GetAsync(dataset.Id, userId.Value, cancellationToken);
        if (grant is null || grant.IsExpired(DateTime.UtcNow))
        {
            return dataset.IsPublic ? DatasetAccess.None with { CanRead = true } : DatasetAccess.None;
        }

        // Any non-expired grant is at least read access.
        return new DatasetAccess(
            CanRead: true,
            CanWrite: grant.AccessLevel is "Write" or "Admin" or "Owner",
            CanDelete: grant.CanDelete,
            CanShare: grant.CanShare,
            IsOwnerOrAdmin: false,
            ExpiresAt: grant.ExpiresAt);
    }
}
