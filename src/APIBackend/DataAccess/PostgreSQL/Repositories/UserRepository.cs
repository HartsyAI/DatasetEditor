using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
using Microsoft.EntityFrameworkCore;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>Entity Framework Core implementation of IUserRepository for PostgreSQL.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly DatasetStudioDbContext _dbContext;

    public UserRepository(DatasetStudioDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Guid> CreateAsync(UserDto user, CancellationToken cancellationToken = default)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        var entity = new UserEntity
        {
            Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id,
            Username = user.Username,
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SecurityStamp = string.IsNullOrEmpty(user.SecurityStamp) ? NewSecurityStamp() : user.SecurityStamp
        };

        _dbContext.Users.Add(entity);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // The case-insensitive unique indexes on username and email: another registration
            // with the same name got there first (audit A-4).
            _dbContext.Entry(entity).State = EntityState.Detached;
            throw new DuplicateUserException(ex);
        }

        return entity.Id;
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<UserDto?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        string lower = username.ToLowerInvariant();
        var entity = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == lower, cancellationToken);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<UserDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string lower = email.ToLowerInvariant();
        var entity = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == lower, cancellationToken);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task UpdateAsync(UserDto user, CancellationToken cancellationToken = default)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        // AsTracking(): same fetch-mutate-SaveChanges landmine as DatasetRepository -- the
        // DbContext's global NoTracking default silently drops these assignments otherwise.
        var entity = await _dbContext.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"User with ID {user.Id} not found");
        }

        entity.Username = user.Username;
        entity.Email = user.Email;
        entity.PasswordHash = user.PasswordHash;
        entity.DisplayName = user.DisplayName;
        entity.Role = user.Role;
        entity.IsActive = user.IsActive;
        entity.EmailVerified = user.EmailVerified;
        entity.LastLoginAt = user.LastLoginAt;
        entity.SecurityStamp = user.SecurityStamp;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static UserDto MapToDto(UserEntity entity) => new()
    {
        Id = entity.Id,
        Username = entity.Username,
        Email = entity.Email,
        PasswordHash = entity.PasswordHash,
        DisplayName = entity.DisplayName,
        Role = entity.Role,
        IsActive = entity.IsActive,
        EmailVerified = entity.EmailVerified,
        CreatedAt = entity.CreatedAt,
        LastLoginAt = entity.LastLoginAt,
        UpdatedAt = entity.UpdatedAt,
        SecurityStamp = entity.SecurityStamp
    };

    /// <summary>A fresh random security stamp.</summary>
    public static string NewSecurityStamp() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
}
