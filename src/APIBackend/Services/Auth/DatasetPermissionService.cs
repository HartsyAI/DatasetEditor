using System.Security.Claims;
using DatasetStudio.Core.Abstractions.Repositories;
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
        return await CanAccessAsync(dataset, user, requireWrite: false, requireDelete: false, cancellationToken);
    }

    public Task<bool> CanWriteAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        CanAccessAsync(dataset, user, requireWrite: true, requireDelete: false, cancellationToken);

    public Task<bool> CanDeleteAsync(DatasetDto dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        CanAccessAsync(dataset, user, requireWrite: false, requireDelete: true, cancellationToken);

    private async Task<bool> CanAccessAsync(
        DatasetDto dataset, ClaimsPrincipal? user, bool requireWrite, bool requireDelete, CancellationToken cancellationToken)
    {
        Guid? userId = user.GetUserId();
        if (userId is null)
        {
            return false; // not authenticated -- private-dataset access always requires a real account
        }

        if (user.IsSiteAdmin())
        {
            return true;
        }
        if (dataset.CreatedByUserId == userId)
        {
            return true; // owner
        }

        PermissionDto? grant = await _permissionRepository.GetAsync(dataset.Id, userId.Value, cancellationToken);
        if (grant is null || grant.IsExpired(DateTime.UtcNow))
        {
            return false;
        }

        if (requireDelete)
        {
            return grant.CanDelete;
        }
        if (requireWrite)
        {
            return grant.AccessLevel is "Write" or "Admin" or "Owner";
        }
        return true; // any non-expired grant is at least read access
    }
}
