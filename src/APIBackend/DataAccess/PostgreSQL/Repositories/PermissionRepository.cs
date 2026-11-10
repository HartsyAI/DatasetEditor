using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>Entity Framework Core implementation of IPermissionRepository for PostgreSQL.</summary>
public sealed class PermissionRepository : IPermissionRepository
{
    private readonly DatasetStudioDbContext _dbContext;

    public PermissionRepository(DatasetStudioDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PermissionDto?> GetAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default)
    {
        PermissionEntity? entity = await _dbContext.Permissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.DatasetId == datasetId && p.UserId == userId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return new PermissionDto
        {
            Id = entity.Id,
            DatasetId = entity.DatasetId,
            UserId = entity.UserId,
            AccessLevel = entity.AccessLevel,
            CanShare = entity.CanShare,
            CanDelete = entity.CanDelete,
            ExpiresAt = entity.ExpiresAt,
            GrantedAt = entity.GrantedAt,
            GrantedByUserId = entity.GrantedByUserId
        };
    }

    public async Task<List<DatasetShareDto>> ListSharesAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.DatasetId == datasetId)
            .OrderBy(p => p.User.Username)
            .Select(p => new
            {
                p.UserId,
                p.User.Username,
                p.User.DisplayName,
                p.AccessLevel,
                p.CanDelete,
                p.CanShare,
                p.ExpiresAt,
                GrantedAt = p.UpdatedAt ?? p.GrantedAt,
                GrantedByUsername = p.GrantedByUser != null ? p.GrantedByUser.Username : null
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatasetShareDto
        {
            UserId = r.UserId,
            Username = r.Username,
            DisplayName = r.DisplayName,
            AccessLevel = ToAccessLevel(r.AccessLevel),
            CanDelete = r.CanDelete,
            CanShare = r.CanShare,
            ExpiresAt = r.ExpiresAt,
            GrantedAt = r.GrantedAt,
            GrantedByUsername = r.GrantedByUsername
        }).ToList();
    }

    public async Task<bool> UpsertAsync(PermissionDto grant, Guid? onlyReplacingGrantsBy, CancellationToken cancellationToken = default)
    {
        // One statement, so the "only replace your own grants" rule holds under concurrency. Read
        // first and written after, a delegated sharer racing the owner to share with the same
        // account could pass the check, then overwrite the owner's new grant and take it over.
        // The unique (dataset_id, user_id) index makes a simultaneous first share an update of the
        // row the other request inserted, subject to the same rule.
        DateTime now = DateTime.UtcNow;
        List<NpgsqlParameter> parameters = new()
        {
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() },
            new NpgsqlParameter("dataset", NpgsqlDbType.Uuid) { Value = grant.DatasetId },
            new NpgsqlParameter("user", NpgsqlDbType.Uuid) { Value = grant.UserId },
            new NpgsqlParameter("level", NpgsqlDbType.Varchar) { Value = grant.AccessLevel },
            new NpgsqlParameter("share", NpgsqlDbType.Boolean) { Value = grant.CanShare },
            new NpgsqlParameter("delete", NpgsqlDbType.Boolean) { Value = grant.CanDelete },
            new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = (object?)grant.ExpiresAt ?? DBNull.Value },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now },
            new NpgsqlParameter("grantor", NpgsqlDbType.Uuid) { Value = (object?)grant.GrantedByUserId ?? DBNull.Value }
        };
        // The restriction is a parameter, not SQL text: with @only null, any existing grant is
        // replaced; otherwise only one granted by @only.
        parameters.Add(new NpgsqlParameter("only", NpgsqlDbType.Uuid) { Value = (object?)onlyReplacingGrantsBy ?? DBNull.Value });

        const string upsert = """
            INSERT INTO permissions (id, dataset_id, user_id, access_level, can_share, can_delete, expires_at, granted_at, granted_by_user_id)
            VALUES (@id, @dataset, @user, @level, @share, @delete, @expires, @now, @grantor)
            ON CONFLICT (dataset_id, user_id) DO UPDATE SET
                access_level = EXCLUDED.access_level,
                can_share = EXCLUDED.can_share,
                can_delete = EXCLUDED.can_delete,
                expires_at = EXCLUDED.expires_at,
                granted_by_user_id = EXCLUDED.granted_by_user_id,
                updated_at = @now
            WHERE @only IS NULL OR permissions.granted_by_user_id = @only
            """;
        int affected = await _dbContext.Database.ExecuteSqlRawAsync(upsert, parameters, cancellationToken);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid datasetId, Guid userId, Guid? onlyIfGrantedBy, CancellationToken cancellationToken = default)
    {
        IQueryable<PermissionEntity> grants = _dbContext.Permissions.Where(p => p.DatasetId == datasetId && p.UserId == userId);
        if (onlyIfGrantedBy is Guid grantor)
        {
            grants = grants.Where(p => p.GrantedByUserId == grantor);
        }
        int deleted = await grants.ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <summary>
    /// The stored access level as the wire enum. Rows from before sharing existed may say "Admin"
    /// or "Owner"; the permission check treats those as write access, so they map to Write.
    /// </summary>
    public static DatasetAccessLevel ToAccessLevel(string stored) =>
        stored is "Write" or "Admin" or "Owner" ? DatasetAccessLevel.Write : DatasetAccessLevel.Read;
}
