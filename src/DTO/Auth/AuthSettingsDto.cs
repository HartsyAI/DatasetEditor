namespace DatasetStudio.DTO.Auth;

/// <summary>Public account settings the sign-in pages read before showing a form.</summary>
public sealed record AuthSettingsDto
{
    /// <summary>Whether the server accepts new registrations (Auth:AllowRegistration).</summary>
    public bool AllowRegistration { get; init; }
}
