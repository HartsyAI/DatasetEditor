using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
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
        var entity = await _dbContext.Permissions
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
}
