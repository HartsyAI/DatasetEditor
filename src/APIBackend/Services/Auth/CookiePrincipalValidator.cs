using System.Security.Claims;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>
/// Re-checks a cookie session against the account. The role and active flag used to be frozen in
/// the cookie for its seven-day life, so deactivating or demoting someone did nothing until it
/// expired (audit A-3). Now a session is rejected once its account is gone, inactive, or its
/// security stamp has changed (a password reset), and a changed role is applied to the session.
/// The account is read at most once per <see cref="AuthOptions.RevalidateEvery"/> per user.
/// </summary>
public static class CookiePrincipalValidator
{
    /// <summary>Claim holding the account's security stamp at sign-in.</summary>
    public const string SecurityStampClaim = "security_stamp";

    private sealed record AccountState(bool Exists, bool IsActive, string Role, string SecurityStamp);

    /// <summary>The handler for <c>CookieAuthenticationEvents.OnValidatePrincipal</c>.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ClaimsPrincipal? principal = context.Principal;
        Guid? userId = principal?.GetUserId();
        if (principal is null || userId is null)
        {
            return;
        }

        IServiceProvider services = context.HttpContext.RequestServices;
        IMemoryCache cache = services.GetRequiredService<IMemoryCache>();
        AuthOptions options = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        async Task<AccountState> LoadAsync()
        {
            IUserRepository users = services.GetRequiredService<IUserRepository>();
            UserDto? user = await users.GetByIdAsync(userId.Value, context.HttpContext.RequestAborted);
            return user is null
                ? new AccountState(false, false, string.Empty, string.Empty)
                : new AccountState(true, user.IsActive, user.Role, user.SecurityStamp);
        }

        // A zero interval checks the database on every request.
        AccountState account = options.RevalidateEvery <= TimeSpan.Zero
            ? await LoadAsync()
            : (await cache.GetOrCreateAsync($"auth-account:{userId}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = options.RevalidateEvery;
                return LoadAsync();
            }))!;

        string sessionStamp = principal.FindFirstValue(SecurityStampClaim) ?? string.Empty;
        if (!account.Exists || !account.IsActive || !string.Equals(sessionStamp, account.SecurityStamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        string sessionRole = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!string.Equals(sessionRole, account.Role, StringComparison.Ordinal))
        {
            ClaimsIdentity identity = new(
                principal.Claims.Where(c => c.Type != ClaimTypes.Role).Append(new Claim(ClaimTypes.Role, account.Role)),
                CookieAuthenticationDefaults.AuthenticationScheme);
            context.ReplacePrincipal(new ClaimsPrincipal(identity));
            context.ShouldRenew = true;
        }
    }

    /// <summary>Drops a cached account so the next request re-reads it (after a change in this process).</summary>
    public static void Forget(IMemoryCache cache, Guid userId) => cache.Remove($"auth-account:{userId}");
}
