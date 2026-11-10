using System.Diagnostics;
using DatasetStudio.APIBackend.Services.Auth;

namespace DatasetStudio.APIBackend.Configuration;

/// <summary>
/// One log line per API request: method, route, dataset, status, time and caller. The API
/// logged no requests at all, so a failure report couldn't be matched to what was asked
/// (audit E-5). The route is the endpoint's pattern (<c>/api/datasets/{datasetId:guid}/files/{*filePath}</c>),
/// not the requested path, and the query string is left out, so file names and search terms (user
/// content) don't reach the log. Image and thumbnail fetches, a hundred per grid page, are logged at
/// Debug; set <c>Logging:LogLevel:DatasetStudio.Requests</c> to change what is kept.
/// </summary>
public static class RequestLogging
{
    /// <summary>The logger category of request lines.</summary>
    public const string Category = "DatasetStudio.Requests";

    /// <summary>Adds the request log for <c>/api</c> routes. Call after authentication so the caller is known.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next(context);
                return;
            }

            long started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            catch
            {
                Write(logger, context, StatusCodes.Status500InternalServerError, started);
                throw;
            }
            Write(logger, context, context.Response.StatusCode, started);
        });
    }

    private static void Write(ILogger logger, HttpContext context, int status, long started)
    {
        string route = RouteOf(context);
        LogLevel level = status >= 500 ? LogLevel.Error
            : IsBulkFetch(context.Request.Method, route, status) ? LogLevel.Debug
            : LogLevel.Information;
        if (!logger.IsEnabled(level))
        {
            return;
        }
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        logger.Log(level,
            "{Method} {Route} dataset {DatasetId} -> {Status} in {ElapsedMs:0} ms (user {UserId})",
            context.Request.Method, route, context.Request.RouteValues["datasetId"] ?? "-", status, elapsedMs,
            context.User.GetUserId()?.ToString() ?? "anonymous");
    }

    /// <summary>The matched endpoint's route pattern, or "(unmatched)" for a request no endpoint took.</summary>
    internal static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";

    /// <summary>A successful image or thumbnail GET: the grid makes one per visible item.</summary>
    internal static bool IsBulkFetch(string method, string route, int status) =>
        HttpMethods.IsGet(method)
        && status < 400
        && (route.Contains("/files/", StringComparison.Ordinal) || route.Contains("thumbnail", StringComparison.OrdinalIgnoreCase));
}
