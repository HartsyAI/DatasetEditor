using System.Security.Claims;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.DTO.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>Account endpoints: register, login, logout, and "who am I".</summary>
internal static class AuthEndpoints
{
    internal static void MapAuthEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/login", Login)
            .WithName("Login")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/me", Me)
            .WithName("Me")
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }

    public static async Task<IResult> Register(
        RegisterRequest request, IAuthService authService, HttpContext httpContext, CancellationToken cancellationToken)
    {
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
            new Claim(ClaimTypes.Role, user.Role)
        };
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            claims.Add(new Claim("display_name", user.DisplayName));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true
        });
    }
}
