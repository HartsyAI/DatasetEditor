using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Auth;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Auth;

public sealed class AuthService : IAuthService
{
    private const int MinPasswordLength = 8;

    /// <summary>BCrypt ignores everything after the first 72 bytes, so longer passwords are refused rather than silently truncated.</summary>
    private const int MaxPasswordBytes = 72;

    /// <summary>The users.email column length.</summary>
    private const int MaxEmailLength = 200;

    private const int MaxDisplayNameLength = 100;

    /// <summary>
    /// Letters, digits, '.', '_' and '-'; 3 to 50 characters. No '@', which login uses to tell an
    /// email from a username.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UsernamePattern =
        new("^[A-Za-z0-9_.-]{3,50}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepository userRepository, IPasswordHasher passwordHasher, ILogger<AuthService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthResult.Fail("Username, email, and password are required.");
        }
        // Over-long or malformed input used to reach the database and fail there with a 500 (audit A-5).
        string? invalid = Validate(request);
        if (invalid is not null)
        {
            return AuthResult.Fail(invalid);
        }

        if (await _userRepository.GetByUsernameAsync(request.Username, cancellationToken) is not null)
        {
            return AuthResult.Fail("That username is already taken.");
        }
        string email = request.Email.Trim();
        if (await _userRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return AuthResult.Fail("An account with that email already exists.");
        }

        var newUser = new UserDto
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = email,
            // Set here rather than left to the repository: the returned user goes straight into
            // the sign-in cookie, and a session whose stamp doesn't match the account is rejected.
            SecurityStamp = DataAccess.PostgreSQL.Repositories.UserRepository.NewSecurityStamp(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName,
            Role = "User",
            IsActive = true,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        Guid createdId;
        try
        {
            createdId = await _userRepository.CreateAsync(newUser, cancellationToken);
        }
        catch (DuplicateUserException)
        {
            // Lost a race with a simultaneous registration of the same name (audit A-4).
            return AuthResult.Fail("That username or email is already taken.");
        }
        _logger.LogInformation("[Auth] Registered new user {UserId}", createdId);

        return AuthResult.Ok(newUser with { Id = createdId });
    }

    /// <summary>The first problem with a registration request, or null when it is valid.</summary>
    internal static string? Validate(RegisterRequest request)
    {
        if (!UsernamePattern.IsMatch(request.Username))
        {
            return "Usernames are 3 to 50 characters: letters, digits, '.', '_' or '-'.";
        }
        string email = request.Email.Trim();
        if (email.Length > MaxEmailLength
            || !System.Net.Mail.MailAddress.TryCreate(email, out System.Net.Mail.MailAddress? address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            return "Enter a valid email address.";
        }
        if (request.Password.Length < MinPasswordLength)
        {
            return $"Password must be at least {MinPasswordLength} characters.";
        }
        if (System.Text.Encoding.UTF8.GetByteCount(request.Password) > MaxPasswordBytes)
        {
            return $"Password must be at most {MaxPasswordBytes} bytes.";
        }
        if (request.DisplayName is { Length: > MaxDisplayNameLength })
        {
            return $"Display name must be at most {MaxDisplayNameLength} characters.";
        }
        return null;
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
            // No name: what was typed may be a mistyped password or someone else's address.
            _logger.LogInformation("[Auth] Failed login: no active account matched");
            return AuthResult.Fail(genericFailure);
        }

        // IPasswordHasher.Verify never throws, even against the seeded admin row's malformed
        // placeholder hash -- see BCryptPasswordHasher's doc comment.
        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Repeated wrong passwords for one account are worth seeing (audit E-5).
            _logger.LogWarning("[Auth] Failed login for user {UserId}: wrong password", user.Id);
            return AuthResult.Fail(genericFailure);
        }

        UserDto updated = user with { LastLoginAt = DateTime.UtcNow };
        await _userRepository.UpdateAsync(updated, cancellationToken);

        // The account id identifies the user; the username is personal data the log doesn't need (audit E-5).
        _logger.LogInformation("[Auth] User {UserId} logged in", user.Id);
        return AuthResult.Ok(updated);
    }
}
