namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>What one principal may do with one dataset (see <see cref="IDatasetPermissionService.GetAccessAsync"/>).</summary>
/// <param name="CanRead">May view the dataset and its items.</param>
/// <param name="CanWrite">May edit them.</param>
/// <param name="CanDelete">May delete the dataset.</param>
/// <param name="CanShare">May manage shares.</param>
/// <param name="IsOwnerOrAdmin">
/// The owner or a site admin. Only they may change or revoke shares someone else granted; a
/// user sharing through their own share manages only the shares they granted.
/// </param>
public sealed record DatasetAccess(bool CanRead, bool CanWrite, bool CanDelete, bool CanShare, bool IsOwnerOrAdmin)
{
    /// <summary>No access.</summary>
    public static DatasetAccess None { get; } = new(false, false, false, false, false);

    /// <summary>Owner or admin: everything.</summary>
    public static DatasetAccess Full { get; } = new(true, true, true, true, true);
}
