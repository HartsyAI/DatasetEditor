using DatasetStudio.DTO.Auth;

namespace DatasetStudio.APIBackend.Services.Auth;

public sealed record AuthResult(bool Succeeded, UserDto? User, string? Error)
{
    public static AuthResult Ok(UserDto user) => new(true, user, null);
    public static AuthResult Fail(string error) => new(false, null, error);
}
