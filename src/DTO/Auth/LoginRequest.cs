namespace DatasetStudio.DTO.Auth;

/// <summary>Request body for POST /api/auth/login. Accepts either a username or an email.</summary>
public sealed record LoginRequest(string UsernameOrEmail, string Password);
