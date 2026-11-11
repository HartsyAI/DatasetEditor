using System.Security.Claims;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>
/// Central authorization decision point for dataset access, checked against ownership
/// (DatasetRecord.CreatedByUserId), the site-wide Admin role, and per-user PermissionEntity grants
/// (respecting ExpiresAt). Endpoints should go through this rather than re-deriving the same
/// owner/admin/permission logic inline.
/// </summary>
public interface IDatasetPermissionService
{
    /// <summary>True if this principal (possibly anonymous) may view the dataset and its items.</summary>
    Task<bool> CanReadAsync(DatasetRecord dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>True if this principal may mutate the dataset's contents (upload/import/edit items).</summary>
    Task<bool> CanWriteAsync(DatasetRecord dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>True if this principal may delete the dataset.</summary>
    Task<bool> CanDeleteAsync(DatasetRecord dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything this principal may do with the dataset, in one lookup. Sharing endpoints need
    /// all of it: a user who shares onward can't hand out more than they hold.
    /// </summary>
    Task<DatasetAccess> GetAccessAsync(DatasetRecord dataset, ClaimsPrincipal? user, CancellationToken cancellationToken = default);
}
