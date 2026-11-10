using DatasetStudio.APIBackend.Configuration;
using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Extensions;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// appsettings*.json live under Configuration/ (non-standard — WebApplication.CreateBuilder's
// default loading only looks in the content root), so the whole chain is rebuilt here rather
// than just appending the two files on top of whatever CreateBuilder already added. Two bugs
// that caused: (1) this always loaded appsettings.Development.json by literal file name,
// regardless of the actual environment, so even a Production deployment picked up dev
// settings; (2) because these two sources were appended LAST, they always outranked
// environment variables (added earlier, inside CreateBuilder) for the same keys — so
// ConnectionStrings__DatasetStudio-style env var overrides, which Docker/production secrets
// injection depends on (see Phase 6) and WebApplicationFactory-based integration tests both
// rely on, were silently ineffective no matter how they were set.
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("Configuration/appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"Configuration/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    // Two separate calls, not one: WebApplicationBuilder.CreateBuilder's own default chain
    // (wiped by Sources.Clear() above) normally adds an ASPNETCORE_-prefixed environment
    // provider specifically so ASPNETCORE_URLS maps to the "urls" config key Kestrel's address
    // binding reads -- a plain AddEnvironmentVariables() call reads it as the literal key
    // "ASPNETCORE_URLS" instead, which nothing looks up, so the standard Docker convention for
    // setting the listen address silently did nothing (Kestrel kept binding to appsettings.json's
    // hardcoded dev "Urls" value no matter what ASPNETCORE_URLS was set to). The plain call
    // below still needs to stay for this app's own non-prefixed env vars
    // (ConnectionStrings__DatasetStudio, ADMIN_PASSWORD, Storage__S3StorageProfiles__..., etc).
    .AddEnvironmentVariables(prefix: "ASPNETCORE_")
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Fail fast when no database connection string is configured. The base appsettings.json used to
// carry a placeholder password, so any run outside Development started and then died at the first
// query with Npgsql 28P01 (audit C-1). The base file no longer has a connection string: Development
// gets its local database from the overlay, and every other environment must supply
// ConnectionStrings__DatasetStudio explicitly.
string? databaseConnectionString = builder.Configuration.GetConnectionString("DatasetStudio");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DatasetStudio' is not configured. Set ConnectionStrings__DatasetStudio " +
        "(see .env.example and docs/DEPLOYMENT.md), or run with ASPNETCORE_ENVIRONMENT=Development " +
        "to use the local development database.");
}

// Request bodies are capped at 10 MB for every endpoint. Only the dataset upload route accepts files
// larger than that: the middleware below raises the cap for that one path before the body is read.
// Before this, every endpoint accepted 5 GB bodies (audit C-6).

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = UploadRequestBodyLimit.DefaultMaxRequestBodyBytes;
});

// Form parsing is only reached by the upload route, so this limit applies only to it. The value and
// header length guards stay at ASP.NET Core's defaults: setting them to int.MaxValue removed a DoS
// guard that no upload needs (audit C-6).
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = UploadRequestBodyLimit.MaxUploadBodyBytes;
});

// Without this, the cookie-auth encryption keys ASP.NET Core generates default to an
// in-container path (found live: /root/.aspnet/DataProtection-Keys) that isn't persisted --
// every container restart/redeploy generates a fresh key ring, silently invalidating every
// existing session cookie and forcing every logged-in user to re-authenticate. Same
// Storage:* config convention as the other storage paths; the Dockerfile's runtime stage
// switches to a non-root "app" user (see below), so this can't just default to the ambient
// /root path either way.
string dataProtectionKeysPath = builder.Configuration["Storage:DataProtectionKeysPath"] ?? "./data/keys";
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("DatasetStudio");

// AddDatasetServices runs before builder.Build(), so there's no DI container yet to resolve a
// logger from. It only needs a logger, so it gets one from a short-lived logger factory instead
// of a throwaway service provider (ASP0000), as does the extension loader below.
using (ILoggerFactory bootstrapLoggerFactory = CreateBootstrapLoggerFactory(builder.Configuration))
{
    builder.Services.AddDatasetServices(builder.Configuration, builder.Environment, bootstrapLoggerFactory.CreateLogger("Startup"));
}
builder.Services.AddAuthServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cookie auth: the client is same-origin in production (the API self-hosts the published WASM
// app, see UseBlazorFrameworkFiles below), but in dev the client's own dev server (5002) and
// the API (5000) are different origins on the same "site" (both localhost, different ports) --
// SameSite=Lax cookies still flow for that case, but only once CORS AllowCredentials() is set
// (below) AND the client opts every request into credentials explicitly (Blazor WASM's
// HttpClient does not send cookies cross-origin by default -- see ClientApp's
// CookieCredentialsHandler).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "DatasetStudio.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // SameAsRequest (not Always): dev runs over plain http://localhost, and a cookie
        // marked Secure is silently dropped by the browser on a non-https request -- that
        // would make login "succeed" (200 + Set-Cookie in the response) while every
        // subsequent authenticated call still 401s, with nothing in the response to explain
        // why. Phase 6 puts a TLS-terminating proxy in front in production, at which point
        // requests arrive as https and this already does the right (Secure) thing.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // This is an API, not a page app -- an unauthenticated/forbidden request should get a
        // plain status code, not a 302 redirect to a login page that doesn't exist server-side.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // Deactivation, demotion and password resets take effect within Auth:RevalidateEvery
        // instead of after the cookie's 7-day life (audit A-3).
        options.Events.OnValidatePrincipal = CookiePrincipalValidator.ValidateAsync;
    });
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

// Behind the Caddy proxy every request comes from the proxy's address, so the client IP the rate
// limits key on comes from X-Forwarded-For, believed only from the proxies configured under
// ReverseProxy (see ReverseProxySettings). Read when the options are first used, after the host
// is built, so every configuration source counts.
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IConfiguration>(DatasetStudio.APIBackend.Configuration.ReverseProxySettings.Apply);

// Login and registration each cost a BCrypt hash and had no limit, inviting CPU exhaustion and
// password guessing (audit A-2). Limits are per client IP and read from Auth options.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many attempts. Wait a minute and try again." }, cancellationToken);
    };
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = httpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.LoginAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.AddPolicy(AuthEndpoints.RegisterRateLimitPolicy, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = httpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.RegistrationsPerHour,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
});

// Per-caller limits for routes that spend S3 egress or HuggingFace quota (audit S-1, H-2).
builder.Services.AddDatasetStudioRateLimits(builder.Configuration);

// Extensions register their services before Build(), after the API's own so an extension can
// replace a core registration on purpose. Until audit C-5 this built a throwaway service provider
// (a second copy of every singleton, ASP0000) only to get a logger and hand extensions a provider;
// the loader needs just a logger, and extensions get configuration instead of a provider.
// The list is kept for MapEndpoints after Build().
IReadOnlyList<LoadedApiExtension> extensions;
using (ILoggerFactory extensionLoggerFactory = CreateBootstrapLoggerFactory(builder.Configuration))
{
    ApiExtensionLoader extensionLoader = new(
        extensionLoggerFactory.CreateLogger<ApiExtensionLoader>(),
        builder.Configuration,
        builder.Environment.ContentRootPath);
    extensions = extensionLoader.ConfigureServices(extensionLoader.LoadAll(), builder.Services);
}

string corsPolicyName = "DatasetEditorClient";
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length == 0)
{
    // Login/session cookies require CORS AllowCredentials(), and AllowAnyOrigin() +
    // AllowCredentials() together are illegal per the CORS spec (the browser rejects it
    // outright) -- so the old "fall back to AllowAnyOrigin() when unconfigured" behavior
    // would make every credentialed request fail with an opaque CORS error at request time,
    // not a config warning at startup. A deployment that clears Cors:AllowedOrigins should
    // fail loudly and immediately instead.
    throw new InvalidOperationException(
        "Cors:AllowedOrigins must list at least one explicit origin. AllowAnyOrigin() is " +
        "incompatible with the credentialed (cookie) authentication this app requires.");
}
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});
WebApplication app = builder.Build();

// First, so everything after (rate limits, HTTPS detection for the cookie) sees the real client.
app.UseForwardedHeaders();

// Apply EF Core migrations on startup so the PostgreSQL schema exists. Requires the
// database to be reachable (run `docker compose up -d` first).
using (IServiceScope migrationScope = app.Services.CreateScope())
{
    ILogger<Program> startupLogger = migrationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        DatasetStudioDbContext db = migrationScope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
        db.Database.Migrate();
        startupLogger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex,
            "Failed to apply database migrations. Is PostgreSQL running? Try `docker compose up -d`.");
        throw;
    }

    // Must run after migrations (needs the users table to exist) and before the app starts
    // accepting requests (a login attempt racing an unrepaired seeded admin row would fail
    // unpredictably depending on timing).
    IAdminBootstrapper adminBootstrapper = migrationScope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
    await adminBootstrapper.RunAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Raises the request-body cap for the dataset upload route and no other. ASP.NET Core only allows the
// cap to change until the body is first read, so this runs before routing and model binding.
app.Use(async (HttpContext context, RequestDelegate next) =>
{
    if (UploadRequestBodyLimit.IsDatasetUploadPath(context.Request.Path.Value ?? string.Empty))
    {
        IHttpMaxRequestBodySizeFeature? bodySizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeLimit is { IsReadOnly: false })
        {
            bodySizeLimit.MaxRequestBodySize = UploadRequestBodyLimit.MaxUploadBodyBytes;
        }
    }

    await next(context);
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(corsPolicyName);
app.UseAuthentication();
// After authentication so the caller is known, and before authorization and the rate limiter so
// their 401/403/429 answers are logged too (audit E-5).
DatasetStudio.APIBackend.Configuration.RequestLogging.UseRequestLogging(app);
app.UseAuthorization();
// After authentication so the limiter can partition by signed-in user; after routing so it sees
// each endpoint's policy.
app.UseRateLimiter();

// Each extension's endpoints go in an authenticated route group at /api/extensions/{id}.
ApiExtensionLoader.MapEndpoints(app, extensions, app.Services.GetRequiredService<ILogger<Program>>());

// Map all endpoints
app.MapAuthEndpoints();
app.MapDatasetEndpoints();
app.MapDatasetShareEndpoints();
app.MapItemEditEndpoints();

// Unknown /api routes answer 404 instead of falling through to the SPA shell (audit C-8). These are
// ordinary endpoints, so they take precedence over the file fallback below.
app.MapMethods(
        "/api/{*path}",
        [HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete, HttpMethods.Options],
        () => Results.NotFound())
    .ExcludeFromDescription();

// Everything that is neither an API route nor a real static file gets the Blazor app shell.
app.MapFallbackToFile("{*path:nonfile}", "index.html");

app.Run();

// A console logger for code that runs before Build(), when there is no DI container to resolve
// one from. Callers dispose it once their startup step is done.
static ILoggerFactory CreateBootstrapLoggerFactory(IConfiguration configuration) =>
    LoggerFactory.Create(logging =>
    {
        logging.AddConfiguration(configuration.GetSection("Logging"));
        logging.AddConsole();
    });

// Top-level statements compile into a `private` Program class by default, which
// WebApplicationFactory<Program> (Microsoft.AspNetCore.Mvc.Testing) can't target from the
// test assembly. This partial declaration makes it public/reachable for integration tests
// without changing anything about how the app itself runs.
public partial class Program { }
