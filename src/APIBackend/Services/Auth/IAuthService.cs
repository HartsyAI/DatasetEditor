using DatasetStudio.DTO.Auth;

namespace DatasetStudio.APIBackend.Services.Auth;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates credentials and stamps LastLoginAt on success. Never throws on bad credentials.</summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
