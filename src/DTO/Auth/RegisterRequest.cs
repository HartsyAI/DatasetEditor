namespace DatasetStudio.DTO.Auth;

/// <summary>Request body for POST /api/auth/register.</summary>
public sealed record RegisterRequest(string Username, string Email, string Password, string? DisplayName = null);
