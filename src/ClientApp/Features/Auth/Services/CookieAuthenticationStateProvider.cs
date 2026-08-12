using System.Security.Claims;
using DatasetStudio.DTO.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace DatasetStudio.ClientApp.Features.Auth.Services;

/// <summary>
/// Backs Blazor's CascadingAuthenticationState / AuthorizeView by asking the API's /api/auth/me
/// (which reads the session cookie server-side) rather than decoding anything client-side --
/// the cookie is HttpOnly and never readable from JS/WASM, so the server is the only source of
/// truth for "who is this". Login.razor/Register.razor call NotifyUserAuthenticated after a
/// successful sign-in and the logout button calls NotifyUserLoggedOut, both without waiting for
/// a fresh /me round trip.
/// </summary>
public sealed class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly AuthApiClient _authApiClient;

    public CookieAuthenticationStateProvider(AuthApiClient authApiClient)
    {
        _authApiClient = authApiClient ?? throw new ArgumentNullException(nameof(authApiClient));
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        AuthUserDto? user = await _authApiClient.GetMeAsync();
        ClaimsPrincipal principal = user is null ? Anonymous : BuildPrincipal(user);
        return new AuthenticationState(principal);
    }

    public void NotifyUserAuthenticated(AuthUserDto user) =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(BuildPrincipal(user))));

    public void NotifyUserLoggedOut() =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));

    private static ClaimsPrincipal BuildPrincipal(AuthUserDto user)
    {
        List<Claim> claims = new()
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        // authenticationType non-null is what makes Identity.IsAuthenticated true.
        var identity = new ClaimsIdentity(claims, authenticationType: "ServerCookie");
        return new ClaimsPrincipal(identity);
    }
}
