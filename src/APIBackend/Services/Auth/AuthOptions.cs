namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>
/// Account settings (configuration section <c>Auth</c>). Registration used to be open to anyone
/// with no limits, and every signed-in user can upload 5 GB and start HuggingFace downloads
/// (audit A-1, A-2).
/// </summary>
public sealed class AuthOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Whether anyone may create an account. Off by default; the Development overlay turns it on.
    /// With it off, accounts come from the ADMIN_* bootstrap.
    /// </summary>
    public bool AllowRegistration { get; set; }

    /// <summary>Login attempts allowed per client IP per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>Registrations allowed per client IP per hour.</summary>
    public int RegistrationsPerHour { get; set; } = 20;

    /// <summary>
    /// How long a signed-in session trusts what it knows about the account (active, role, security
    /// stamp) before checking the database again.
    /// </summary>
    public TimeSpan RevalidateEvery { get; set; } = TimeSpan.FromSeconds(30);
}
