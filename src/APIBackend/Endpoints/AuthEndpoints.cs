using System.Security.Claims;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.DTO.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>Account endpoints: register, login, logout, and "who am I".</summary>
internal static class AuthEndpoints
{
    /// <summary>Rate-limiter policy on login (per client IP, see Program.cs).</summary>
    internal const string LoginRateLimitPolicy = "auth-login";

    /// <summary>Rate-limiter policy on registration (per client IP, see Program.cs).</summary>
    internal const string RegisterRateLimitPolicy = "auth-register";

    internal static void MapAuthEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RegisterRateLimitPolicy);

        group.MapPost("/login", Login)
            .WithName("Login")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(LoginRateLimitPolicy);

        group.MapGet("/settings", Settings)
            .WithName("AuthSettings")
            .Produces<AuthSettingsDto>(StatusCodes.Status200OK);

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/me", Me)
            .WithName("Me")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }

    /// <summary>What the sign-in pages need to know before showing a form (public).</summary>
    public static IResult Settings(IOptions<AuthOptions> options)
    {
        return Results.Ok(new AuthSettingsDto { AllowRegistration = options.Value.AllowRegistration });
    }

    public static async Task<IResult> Register(
        RegisterRequest request, IAuthService authService, IOptions<AuthOptions> options, HttpContext httpContext, CancellationToken cancellationToken)
    {
        // Registration used to be open to anyone on the internet (audit A-1).
        if (!options.Value.AllowRegistration)
        {
            return Results.Json(
                new { error = "Registration is closed on this server. Ask an administrator for an account." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        AuthResult result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded || result.User is null)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        await SignInAsync(httpContext, result.User);
        return Results.Ok(AuthUserDto.FromUserDto(result.User));
    }

    public static async Task<IResult> Login(
        LoginRequest request, IAuthService authService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        AuthResult result = await authService.LoginAsync(request, cancellationToken);
        if (!result.Succeeded || result.User is null)
        {
            return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAsync(httpContext, result.User);
        return Results.Ok(AuthUserDto.FromUserDto(result.User));
    }

    // Takes a (harmlessly unused) CancellationToken alongside HttpContext deliberately: a
    // handler shaped exactly like Func<HttpContext, Task<T>> is ambiguously convertible to
    // both the typed minimal-API Delegate overload (returns RouteHandlerBuilder, what we want)
    // and the older single-parameter RequestDelegate overload (returns plain
    // IEndpointConventionBuilder, no .Produces()) -- C# picks the more specific RequestDelegate
    // match, silently breaking the fluent chain below. A second parameter rules that overload
    // out entirely.
    public static async Task<IResult> Logout(HttpContext httpContext, CancellationToken cancellationToken)
    {
        // Idempotent and unconditional -- no RequireAuthorization() here, since "log out when
        // already logged out" should just succeed rather than 401.
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok();
    }

    public static IResult Me(HttpContext httpContext)
    {
        ClaimsPrincipal user = httpContext.User;
        Guid? userId = user.GetUserId();
        if (userId is null)
        {
            // RequireAuthorization() on this route means the auth middleware already rejects
            // an unauthenticated request before this ever runs; this is just defensive.
            return Results.Unauthorized();
        }

        return Results.Ok(new AuthUserDto
        {
            Id = userId.Value,
            Username = user.Identity?.Name ?? string.Empty,
            Email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            DisplayName = user.FindFirstValue("display_name"),
            Role = user.FindFirstValue(ClaimTypes.Role) ?? "User"
        });
    }

    private static async Task SignInAsync(HttpContext httpContext, UserDto user)
    {
        List<Claim> claims = new()
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(CookiePrincipalValidator.SecurityStampClaim, user.SecurityStamp)
        };
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            claims.Add(new Claim("display_name", user.DisplayName));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // The new cookie was built from the account as it is now. An older cached copy would
        // otherwise overwrite its role on the next request until the cache entry expired.
        CookiePrincipalValidator.Forget(httpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), user.Id);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true
        });
    }
}
