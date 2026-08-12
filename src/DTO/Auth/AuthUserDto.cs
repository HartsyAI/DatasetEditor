namespace DatasetStudio.DTO.Auth;

/// <summary>Public-safe user projection — no password hash. Returned by register/login/me.</summary>
public sealed record AuthUserDto
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string Role { get; init; } = "User";

    public static AuthUserDto FromUserDto(UserDto user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role
    };
}
