using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>
/// Runs once at startup (after migrations) to guarantee a usable admin account.
///
/// This deliberately does NOT check "is the users table empty" -- the InitialCreate migration
/// always seeds one admin row (id 00000000-0000-0000-0000-000000000001) with a literal
/// placeholder string as its password hash ("$2a$11$placeholder_hash_replace_on_first_run"),
/// which is not valid BCrypt output. An empty-table check would see that row, conclude
/// bootstrap isn't needed, and leave an admin account nobody can ever log into. Instead this
/// looks up the configured admin username directly and, if found, re-syncs its password hash
/// (and role/active/verified flags) from ADMIN_PASSWORD every boot -- idempotent, and it doubles
/// as "reset the admin password by changing the env var and restarting".
/// </summary>
public sealed class AdminBootstrapper : IAdminBootstrapper
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminBootstrapper> _logger;

    public AdminBootstrapper(IUserRepository userRepository, IPasswordHasher passwordHasher, IConfiguration configuration, ILogger<AdminBootstrapper> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        string? adminPassword = _configuration["ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogInformation("[Auth] ADMIN_PASSWORD not configured -- skipping admin bootstrap. " +
                "If the seeded admin account's placeholder password was never repaired, it remains permanently unusable (by design, not a bug).");
            return;
        }

        string adminUsername = _configuration["ADMIN_USERNAME"] ?? "admin";
        string adminEmail = _configuration["ADMIN_EMAIL"] ?? "admin@localhost";
        string newHash = _passwordHasher.Hash(adminPassword);

        UserDto? existing = await _userRepository.GetByUsernameAsync(adminUsername, cancellationToken);
        if (existing is null)
        {
            var admin = new UserDto
            {
                Id = Guid.NewGuid(),
                Username = adminUsername,
                Email = adminEmail,
                PasswordHash = newHash,
                Role = "Admin",
                IsActive = true,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            await _userRepository.CreateAsync(admin, cancellationToken);
            _logger.LogInformation("[Auth] Bootstrapped new admin account '{AdminUsername}'", adminUsername);
            return;
        }

        UserDto updated = existing with
        {
            PasswordHash = newHash,
            Email = adminEmail,
            Role = "Admin",
            IsActive = true,
            EmailVerified = true
        };
        await _userRepository.UpdateAsync(updated, cancellationToken);
        _logger.LogInformation("[Auth] Synced admin account '{AdminUsername}' from ADMIN_* configuration", adminUsername);
    }
}
