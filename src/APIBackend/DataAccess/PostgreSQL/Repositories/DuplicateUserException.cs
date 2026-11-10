namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// A user could not be created because the username or email is already taken, compared
/// case-insensitively. Raised from the database's unique index, so two simultaneous
/// registrations of the same name can't both succeed (audit A-4).
/// </summary>
public sealed class DuplicateUserException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DuplicateUserException(Exception inner)
        : base("That username or email is already taken.", inner)
    {
    }
}
