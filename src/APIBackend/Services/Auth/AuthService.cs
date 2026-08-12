using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Auth;

namespace DatasetStudio.APIBackend.Services.Auth;

public sealed class AuthService : IAuthService
{
    private const int MinPasswordLength = 8;

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public AuthService(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthResult.Fail("Username, email, and password are required.");
        }
        if (request.Password.Length < MinPasswordLength)
        {
            return AuthResult.Fail($"Password must be at least {MinPasswordLength} characters.");
        }

        if (await _userRepository.GetByUsernameAsync(request.Username, cancellationToken) is not null)
        {
            return AuthResult.Fail("That username is already taken.");
        }
        if (await _userRepository.GetByEmailAsync(request.Email, cancellationToken) is not null)
        {
            return AuthResult.Fail("An account with that email already exists.");
        }

        var newUser = new UserDto
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName,
            Role = "User",
            IsActive = true,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        Guid createdId = await _userRepository.CreateAsync(newUser, cancellationToken);
        Logs.Info($"[Auth] Registered new user '{request.Username}' ({createdId})");

        return AuthResult.Ok(newUser with { Id = createdId });
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // Deliberately the same generic failure message for "no such user", "account
        // disabled", and "wrong password" -- distinguishing them lets an attacker enumerate
        // valid usernames/emails.
        const string genericFailure = "Invalid username/email or password.";

        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthResult.Fail(genericFailure);
        }

        UserDto? user = request.UsernameOrEmail.Contains('@')
            ? await _userRepository.GetByEmailAsync(request.UsernameOrEmail, cancellationToken)
            : await _userRepository.GetByUsernameAsync(request.UsernameOrEmail, cancellationToken);
        // A username can also legally contain '@' in theory, but this app never generates one
        // that way -- if the email-shaped lookup misses, fall back to a username lookup too.
        user ??= await _userRepository.GetByUsernameAsync(request.UsernameOrEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return AuthResult.Fail(genericFailure);
        }

        // IPasswordHasher.Verify never throws, even against the seeded admin row's malformed
        // placeholder hash -- see BCryptPasswordHasher's doc comment.
        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthResult.Fail(genericFailure);
        }

        UserDto updated = user with { LastLoginAt = DateTime.UtcNow };
        await _userRepository.UpdateAsync(updated, cancellationToken);

        Logs.Info($"[Auth] User '{user.Username}' ({user.Id}) logged in");
        return AuthResult.Ok(updated);
    }
}
