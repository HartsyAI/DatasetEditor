using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using Microsoft.EntityFrameworkCore;

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

    public async Task UpsertAsync(PermissionDto grant, CancellationToken cancellationToken = default)
    {
        // Two attempts: a simultaneous first share with the same user loses the insert race on the
        // (dataset_id, user_id) unique index, and then updates the row the other request created.
        for (int attempt = 0; ; attempt++)
        {
            PermissionEntity? entity = await _dbContext.Permissions
                .AsTracking()
                .FirstOrDefaultAsync(p => p.DatasetId == grant.DatasetId && p.UserId == grant.UserId, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (entity is null)
            {
                entity = new PermissionEntity
                {
                    Id = Guid.NewGuid(),
                    DatasetId = grant.DatasetId,
                    UserId = grant.UserId,
                    GrantedAt = now
                };
                _dbContext.Permissions.Add(entity);
            }
            else
            {
                entity.UpdatedAt = now;
            }
            entity.AccessLevel = grant.AccessLevel;
            entity.CanDelete = grant.CanDelete;
            entity.CanShare = grant.CanShare;
            entity.ExpiresAt = grant.ExpiresAt;
            entity.GrantedByUserId = grant.GrantedByUserId;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 0 && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    public async Task<bool> DeleteAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default)
    {
        int deleted = await _dbContext.Permissions
            .Where(p => p.DatasetId == datasetId && p.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <summary>
    /// The stored access level as the wire enum. Rows from before sharing existed may say "Admin"
    /// or "Owner"; the permission check treats those as write access, so they map to Write.
    /// </summary>
    public static DatasetAccessLevel ToAccessLevel(string stored) =>
        stored is "Write" or "Admin" or "Owner" ? DatasetAccessLevel.Write : DatasetAccessLevel.Read;
}
