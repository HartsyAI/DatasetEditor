using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    // Work factor 11: BCrypt.Net-Next's own default, ~50-100ms per hash on typical hardware --
    // deliberately slow (that's the point of BCrypt) but not so slow it makes login/register
    // requests feel broken.
    private const int WorkFactor = 11;

    private readonly ILogger<BCryptPasswordHasher> _logger;

    public BCryptPasswordHasher(ILogger<BCryptPasswordHasher> logger)
    {
        _logger = logger;
    }

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception ex)
        {
            // BCrypt.Net throws (SaltParseException or similar) on a hash string that isn't
            // well-formed BCrypt output -- the seeded admin row ships with exactly such a
            // placeholder value until the admin bootstrap repairs it. A malformed hash means
            // "this password can never match", not "something went wrong".
            _logger.LogWarning("[Auth] Password verification failed against a malformed hash: {Message}", ex.Message);
            return false;
        }
    }
}
