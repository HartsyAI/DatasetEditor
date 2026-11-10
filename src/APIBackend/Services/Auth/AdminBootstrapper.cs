using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
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
/// looks up the configured admin username directly and, if found, brings its email and
/// role/active/verified flags in line with ADMIN_*, and replaces the password hash only when
/// ADMIN_PASSWORD no longer verifies against it. Changing the env var and restarting still resets
/// the admin password, and ends the sessions signed in with the old one.
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

        await RetireSeededPlaceholderAsync(adminUsername, cancellationToken);

        UserDto? existing = await _userRepository.GetByUsernameAsync(adminUsername, cancellationToken);
        if (existing is null)
        {
            var admin = new UserDto
            {
                Id = Guid.NewGuid(),
                Username = adminUsername,
                Email = adminEmail,
                PasswordHash = _passwordHasher.Hash(adminPassword),
                Role = "Admin",
                IsActive = true,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            await _userRepository.CreateAsync(admin, cancellationToken);
            _logger.LogInformation("[Auth] Bootstrapped new admin account '{AdminUsername}'", adminUsername);
            return;
        }

        // Only what differs is changed. The hash used to be rewritten on every boot (audit A-6);
        // now it changes only when ADMIN_PASSWORD no longer matches, and then the security stamp
        // rotates so sessions signed in with the old password end.
        bool passwordChanged = !_passwordHasher.Verify(adminPassword, existing.PasswordHash);
        UserDto updated = existing with
        {
            PasswordHash = passwordChanged ? _passwordHasher.Hash(adminPassword) : existing.PasswordHash,
            SecurityStamp = passwordChanged ? DataAccess.PostgreSQL.Repositories.UserRepository.NewSecurityStamp() : existing.SecurityStamp,
            Email = adminEmail,
            Role = "Admin",
            IsActive = true,
            EmailVerified = true
        };
        if (updated == existing)
        {
            _logger.LogInformation("[Auth] Admin account '{AdminUsername}' already matches ADMIN_* configuration", adminUsername);
            return;
        }
        await _userRepository.UpdateAsync(updated, cancellationToken);
        _logger.LogInformation("[Auth] Synced admin account '{AdminUsername}' from ADMIN_* configuration (password changed: {PasswordChanged})", adminUsername, passwordChanged);
    }

    /// <summary>
    /// The initial migration seeds an "admin" row with a placeholder hash nobody can log in with.
    /// When ADMIN_USERNAME names a different account, that row used to stay forever, holding the
    /// "admin" name and the admin@localhost email with an unusable password (audit A-6), and the
    /// email collision stopped a new admin being created with the default ADMIN_EMAIL. The row is
    /// kept (it may be referenced), but deactivated and renamed so both values are free.
    /// </summary>
    private async Task RetireSeededPlaceholderAsync(string adminUsername, CancellationToken cancellationToken)
    {
        if (string.Equals(adminUsername, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        UserDto? seeded = await _userRepository.GetByIdAsync(SeededAdminId, cancellationToken);
        if (seeded is null || !seeded.PasswordHash.StartsWith(PlaceholderHashPrefix, StringComparison.Ordinal))
        {
            return;
        }
        // The first name/email pair no other account has. A fixed pair could already belong to an
        // account, and the unique indexes would then fail the update and the API's startup.
        string retiredName;
        for (int n = 1; ; n++)
        {
            retiredName = n == 1 ? RetiredPlaceholderUsername : $"{RetiredPlaceholderUsername}-{n}";
            UserDto? byName = await _userRepository.GetByUsernameAsync(retiredName, cancellationToken);
            UserDto? byEmail = await _userRepository.GetByEmailAsync(retiredName + "@localhost", cancellationToken);
            if ((byName is null || byName.Id == seeded.Id) && (byEmail is null || byEmail.Id == seeded.Id))
            {
                break;
            }
        }
        UserDto retired = seeded with
        {
            IsActive = false,
            Username = retiredName,
            Email = retiredName + "@localhost"
        };
        if (retired != seeded)
        {
            await _userRepository.UpdateAsync(retired, cancellationToken);
            _logger.LogInformation("[Auth] Retired the seeded placeholder admin row; ADMIN_USERNAME is '{AdminUsername}'", adminUsername);
        }
    }

    /// <summary>The name the unused seeded admin row is given, freeing "admin" and admin@localhost.</summary>
    private const string RetiredPlaceholderUsername = "retired-seed-admin";

    /// <summary>Id of the admin row the initial migration seeds.</summary>
    private static readonly Guid SeededAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Start of the seeded row's placeholder hash (see DatasetStudioDbContext.SeedDefaultData).</summary>
    private const string PlaceholderHashPrefix = "$2a$11$placeholder";
}
