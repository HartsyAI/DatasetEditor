using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using IPermissionRepository = DatasetStudio.Core.Abstractions.Repositories.IPermissionRepository;
using IUserRepository = DatasetStudio.Core.Abstractions.Repositories.IUserRepository;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>
/// Sharing a dataset with other accounts (audit D-2). The grant table and the permission checks
/// that read it existed, but nothing could create a grant, so a private dataset could never be
/// shared. The owner, a site admin, or a user whose share includes the share right manages them.
/// </summary>
internal static class DatasetShareEndpoints
{
    /// <summary>Maps the share endpoints under /api/datasets/{datasetId}/shares.</summary>
    internal static void MapDatasetShareEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/datasets/{datasetId:guid}/shares")
            .WithTags("Dataset sharing")
            .RequireAuthorization();

        group.MapGet("/", ListShares)
            .WithName("ListDatasetShares")
            .Produces<List<DatasetShareDto>>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/", ShareDataset)
            .WithName("ShareDataset")
            .Produces<DatasetShareDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{userId:guid}", RevokeShare)
            .WithName("RevokeDatasetShare")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Lists everyone the dataset is shared with. Needs the share right.</summary>
    public static async Task<IResult> ListShares(
        Guid datasetId,
        Core.Abstractions.Repositories.IDatasetRepository datasets,
        IDatasetPermissionService permissions,
        IPermissionRepository grants,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        (DatasetDto? dataset, DatasetAccess access) = await LoadAsync(datasetId, datasets, permissions, httpContext, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }
        if (!access.CanShare)
        {
            return Results.Forbid();
        }
        return Results.Ok(await grants.ListSharesAsync(datasetId, cancellationToken));
    }

    /// <summary>
    /// Shares the dataset with an account, or changes the share it has. A user sharing through
    /// their own share can't grant more than they hold, and can change only shares they granted.
    /// </summary>
    public static async Task<IResult> ShareDataset(
        Guid datasetId,
        ShareDatasetRequest request,
        Core.Abstractions.Repositories.IDatasetRepository datasets,
        IDatasetPermissionService permissions,
        IPermissionRepository grants,
        IUserRepository users,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        (DatasetDto? dataset, DatasetAccess access) = await LoadAsync(datasetId, datasets, permissions, httpContext, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }
        if (!access.CanShare)
        {
            return Results.Forbid();
        }

        string who = request.UsernameOrEmail?.Trim() ?? string.Empty;
        if (who.Length == 0)
        {
            return Error("Enter the username or email of the account to share with.");
        }
        if (!Enum.IsDefined(request.AccessLevel))
        {
            return Error("Access must be Read or Write.");
        }
        if (request.ExpiresAt is DateTime expires && expires.ToUniversalTime() <= DateTime.UtcNow)
        {
            return Error("The end date has to be in the future.");
        }

        UserDto? target = who.Contains('@')
            ? await users.GetByEmailAsync(who, cancellationToken)
            : await users.GetByUsernameAsync(who, cancellationToken);
        if (target is null || !target.IsActive)
        {
            return Results.Json(new { error = "No active account has that username or email." }, statusCode: StatusCodes.Status404NotFound);
        }
        if (target.Id == dataset.CreatedByUserId)
        {
            return Error("That account owns this dataset.");
        }

        Guid callerId = httpContext.User.GetUserId()!.Value;
        if (target.Id == callerId)
        {
            return Error("You can't change your own share.");
        }

        if (!access.IsOwnerOrAdmin)
        {
            if (request.AccessLevel == DatasetAccessLevel.Write && !access.CanWrite)
            {
                return Forbidden("Your own access is read-only, so you can share this dataset only for reading.");
            }
            if (request.CanDelete && !access.CanDelete)
            {
                return Forbidden("You can't let others delete this dataset, because you can't delete it yourself.");
            }
            PermissionDto? existing = await grants.GetAsync(datasetId, target.Id, cancellationToken);
            if (existing is not null && existing.GrantedByUserId != callerId)
            {
                return Forbidden("Someone else shared this dataset with that account; only they or the owner can change it.");
            }
        }

        await grants.UpsertAsync(new PermissionDto
        {
            DatasetId = datasetId,
            UserId = target.Id,
            AccessLevel = request.AccessLevel.ToString(),
            CanDelete = request.CanDelete,
            CanShare = request.CanShare,
            ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
            GrantedByUserId = callerId
        }, cancellationToken);

        List<DatasetShareDto> shares = await grants.ListSharesAsync(datasetId, cancellationToken);
        return Results.Ok(shares.Single(s => s.UserId == target.Id));
    }

    /// <summary>
    /// Ends a share. The owner or an admin may end any; a user with the share right may end the
    /// shares they granted; anyone may end their own (leave the dataset).
    /// </summary>
    public static async Task<IResult> RevokeShare(
        Guid datasetId,
        Guid userId,
        Core.Abstractions.Repositories.IDatasetRepository datasets,
        IDatasetPermissionService permissions,
        IPermissionRepository grants,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        (DatasetDto? dataset, DatasetAccess access) = await LoadAsync(datasetId, datasets, permissions, httpContext, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        Guid callerId = httpContext.User.GetUserId()!.Value;
        PermissionDto? grant = await grants.GetAsync(datasetId, userId, cancellationToken);
        if (grant is null)
        {
            return Results.NotFound();
        }

        bool allowed = access.IsOwnerOrAdmin
            || userId == callerId
            || (access.CanShare && grant.GrantedByUserId == callerId);
        if (!allowed)
        {
            return Results.Forbid();
        }

        await grants.DeleteAsync(datasetId, userId, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// The dataset and the caller's access to it. The dataset comes back null when it doesn't
    /// exist or the caller can't read it, so a private dataset's existence isn't revealed (404,
    /// as everywhere else).
    /// </summary>
    private static async Task<(DatasetDto? Dataset, DatasetAccess Access)> LoadAsync(
        Guid datasetId,
        Core.Abstractions.Repositories.IDatasetRepository datasets,
        IDatasetPermissionService permissions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await datasets.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return (null, DatasetAccess.None);
        }
        DatasetAccess access = await permissions.GetAccessAsync(dataset, httpContext.User, cancellationToken);
        return access.CanRead ? (dataset, access) : (null, access);
    }

    private static IResult Error(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);
}
