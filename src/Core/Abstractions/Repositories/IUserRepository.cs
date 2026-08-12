using DatasetStudio.DTO.Auth;

namespace DatasetStudio.Core.Abstractions.Repositories;

/// <summary>Repository interface for user accounts (PostgreSQL-backed).</summary>
public interface IUserRepository
{
    Task<Guid> CreateAsync(UserDto user, CancellationToken cancellationToken = default);

    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup by username.</summary>
    Task<UserDto?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup by email.</summary>
    Task<UserDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task UpdateAsync(UserDto user, CancellationToken cancellationToken = default);
}
