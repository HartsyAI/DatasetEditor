using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DatasetStudio.APIBackend.Services.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace DatasetStudio.APIBackend.Configuration;

/// <summary>
/// Named rate-limiter policies for routes that cost the server real money or upstream quota per
/// request. Each policy is a fixed one-minute window per caller: the signed-in user when there is
/// one, otherwise the client IP address. Limits come from the <c>RateLimiting</c> configuration
/// section and are read when the limiter is first used.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// GetS3File. A public S3 dataset otherwise lets anyone pull the whole prefix through the
    /// server, paid for with the server's credentials and bandwidth (audit S-1).
    /// </summary>
    public const string S3File = "s3-file";

    /// <summary>
    /// HuggingFace discovery: each request makes several upstream Hub calls with long timeouts
    /// (audit H-2).
    /// </summary>
    public const string HuggingFaceDiscover = "hf-discover";

    /// <summary>The configuration section holding the per-minute limits.</summary>
    public const string SectionName = "RateLimiting";

    private const int DefaultS3FilePermitsPerMinute = 600;
    private const int DefaultHuggingFaceDiscoverPermitsPerMinute = 10;

    /// <summary>
    /// Adds the policies above to the rate limiter. The rejection response (429, Retry-After and a
    /// JSON error) is set once in Program.cs alongside the login and registration policies.
    /// </summary>
    public static IServiceCollection AddDatasetStudioRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(S3File, httpContext => PerCallerPerMinute(
                httpContext, ReadLimit(configuration, "S3FilePermitsPerMinute", DefaultS3FilePermitsPerMinute)));
            options.AddPolicy(HuggingFaceDiscover, httpContext => PerCallerPerMinute(
                httpContext, ReadLimit(configuration, "HuggingFaceDiscoverPermitsPerMinute", DefaultHuggingFaceDiscoverPermitsPerMinute)));
        });
        return services;
    }

    private static RateLimitPartition<string> PerCallerPerMinute(HttpContext httpContext, int permitsPerMinute)
    {
        return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext.User, httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }

    /// <summary>
    /// The signed-in user, so people behind one NAT or proxy don't share a budget; otherwise the
    /// client address. Behind a reverse proxy that address is the proxy's unless forwarded headers
    /// are honoured (docs/DEPLOYMENT.md, "Rate limits").
    /// </summary>
    private static string PartitionKey(ClaimsPrincipal user, HttpContext httpContext)
    {
        Guid? userId = user.GetUserId();
        if (userId is not null)
        {
            return "user:" + userId.Value.ToString("N");
        }
        return "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static int ReadLimit(IConfiguration configuration, string key, int defaultValue)
    {
        int configured = configuration.GetValue($"{SectionName}:{key}", defaultValue);
        // A zero or negative limit would refuse every request; treat it as a misconfiguration.
        return configured > 0 ? configured : defaultValue;
    }
}
