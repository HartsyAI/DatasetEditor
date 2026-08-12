namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>
/// Wraps password hashing so callers never touch a hashing library directly. <see cref="Verify"/>
/// must never throw -- a malformed/corrupt hash in the database (including the seeded admin
/// row's placeholder value before bootstrap repairs it) must be treated as "does not match this
/// password", not surfaced as an unhandled exception that turns a login attempt into a 500.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
