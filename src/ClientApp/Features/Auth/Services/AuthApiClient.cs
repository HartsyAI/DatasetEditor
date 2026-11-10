using System.Net;
using System.Net.Http.Json;
using DatasetStudio.DTO.Auth;

namespace DatasetStudio.ClientApp.Features.Auth.Services;

/// <summary>Thin wrapper over HttpClient for the /api/auth/* endpoints.</summary>
public sealed class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<AuthOutcome> RegisterAsync(string username, string email, string password, string? displayName, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "api/auth/register", new RegisterRequest(username, email, password, displayName), cancellationToken);
        return await ToOutcomeAsync(response, cancellationToken);
    }

    public async Task<AuthOutcome> LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "api/auth/login", new LoginRequest(usernameOrEmail, password), cancellationToken);
        return await ToOutcomeAsync(response, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync("api/auth/logout", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The server's sign-in settings. Null when the server can't be reached, so callers fall back
    /// to showing registration and let the server refuse it.
    /// </summary>
    public async Task<AuthSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AuthSettingsDto>("api/auth/settings", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Null if not authenticated (401) -- not an exception, since "logged out" is an expected, common state.</summary>
    public async Task<AuthUserDto?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/auth/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthUserDto>(cancellationToken: cancellationToken);
    }

    private static async Task<AuthOutcome> ToOutcomeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            AuthUserDto? user = await response.Content.ReadFromJsonAsync<AuthUserDto>(cancellationToken: cancellationToken);
            return user is null ? AuthOutcome.Fail("Unexpected empty response from server.") : AuthOutcome.Ok(user);
        }

        string? error = null;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: cancellationToken);
            error = body?.Error;
        }
        catch
        {
            // Response body wasn't the expected {error} shape -- fall through to the generic message below.
        }
        return AuthOutcome.Fail(error ?? $"Request failed ({(int)response.StatusCode}).");
    }

    private sealed record ErrorBody(string? Error);
}

public sealed record AuthOutcome(bool Succeeded, AuthUserDto? User, string? Error)
{
    public static AuthOutcome Ok(AuthUserDto user) => new(true, user, null);
    public static AuthOutcome Fail(string error) => new(false, null, error);
}
