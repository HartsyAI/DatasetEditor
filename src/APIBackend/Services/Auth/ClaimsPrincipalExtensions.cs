using System.Security.Claims;

namespace DatasetStudio.APIBackend.Services.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Not named IsInRole -- ClaimsPrincipal already declares an instance IsInRole(string), which
    /// wins overload resolution over any same-signature extension and throws on a null instance,
    /// defeating the null-safety this is for. A distinct name avoids that trap entirely.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }
        string? idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out Guid id) ? id : null;
    }

    public static bool IsSiteAdmin(this ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true && principal.IsInRole("Admin");
}
