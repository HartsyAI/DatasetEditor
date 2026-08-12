namespace DatasetStudio.DTO.Auth;

/// <summary>
/// Full user record as read from/written to the repository, including <see cref="PasswordHash"/>.
/// Never serialize this directly to an API response — use <see cref="AuthUserDto"/> (via
/// <c>ToAuthUserDto()</c>) for anything that leaves the server.
/// </summary>
public sealed record UserDto
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string Role { get; init; } = "User";
    public bool IsActive { get; init; } = true;
    public bool EmailVerified { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
