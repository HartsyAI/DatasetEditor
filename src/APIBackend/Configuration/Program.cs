using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Extensions;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
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

// Configure Kestrel to allow large file uploads (5GB)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 5L * 1024 * 1024 * 1024; // 5GB
});

// Configure form options to allow large multipart uploads (5GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5L * 1024 * 1024 * 1024; // 5GB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
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
// logger from -- same constraint the extension-discovery block below solves with its own
// throwaway ServiceProvider. This one is cheaper to build directly since AddDatasetServices
// only needs a logger, not a whole container.
using (ILoggerFactory bootstrapLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    logging.AddConsole();
}))
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
    });
builder.Services.AddAuthorization();

// Extension discovery/ConfigureServices has to run before builder.Build(), so it needs a
// service provider before one otherwise exists, and DiscoverAndLoadAsync's result is used
// again further down (after app = builder.Build()), so both are declared here and assigned
// inside the throwaway-provider scope below.
ApiExtensionRegistry extensionRegistry;
List<IExtension> extensions;

// Build exactly one throwaway provider for pre-Build() needs (previously this built three
// separate ones — each a distinct set of singleton instances discarded seconds later, per
// the ASP0000 warning) and reuse it for both the registry's own logger and any
// per-extension configure-time logging below.
using (ServiceProvider startupServices = builder.Services.BuildServiceProvider())
{
    extensionRegistry = new ApiExtensionRegistry(
        startupServices.GetRequiredService<ILogger<ApiExtensionRegistry>>(),
        builder.Configuration,
        startupServices,
        builder.Environment.ContentRootPath);

    extensions = await extensionRegistry.DiscoverAndLoadAsync();

    // Configure services for each extension
    foreach (var extension in extensions)
    {
        try
        {
            extension.ConfigureServices(builder.Services);
        }
        catch (Exception ex)
        {
            var startupLogger = startupServices.GetRequiredService<ILogger<Program>>();
            startupLogger.LogError(ex, "Failed to configure services for extension: {ExtensionId}",
                extension.GetManifest().Metadata.Id);
        }
    }
}

// Register the already-initialized instance itself (not the type) so that anything later
// resolving ApiExtensionRegistry via DI gets this same discovered/loaded registry, not a
// second, never-discovered instance the container would otherwise construct on first
// injection.
builder.Services.AddSingleton(extensionRegistry);

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

// Routes Core.Utilities.Logging.Logs (used by the shared Core project's ingestion/parsing code,
// which predates real DI-based logging there) through this app's actual configured logging
// instead of its Console.WriteLine fallback. See Logs.cs for why that project isn't fully
// converted to injected ILogger<T> instead.
DatasetStudio.Core.Utilities.Logging.Logs.Initialize(app.Services.GetRequiredService<ILoggerFactory>());

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
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(corsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

// Configure and initialize extensions
var logger = app.Services.GetRequiredService<ILogger<Program>>();
foreach (var extension in extensions)
{
    try
    {
        var extensionId = extension.GetManifest().Metadata.Id;
        logger.LogInformation("Configuring extension: {ExtensionId}", extensionId);

        // Configure app pipeline (API-only hook; client extensions don't implement it)
        if (extension is DatasetStudio.Extensions.SDK.IApiExtension apiExtension)
        {
            apiExtension.ConfigureApp(app);
        }

        // Create extension context
        var context = new ExtensionContextBuilder()
            .WithManifest(extension.GetManifest())
            .WithServices(app.Services)
            .WithConfiguration(builder.Configuration.GetSection($"Extensions:{extensionId}"))
            .WithLogger(app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"Extension.{extensionId}"))
            .WithEnvironment(ExtensionEnvironment.Api)
            .WithExtensionDirectory(extensionRegistry.GetExtension(extensionId)?.Directory ?? "")
            .Build();

        // Initialize extension
        await extension.InitializeAsync(context);

        // Validate extension
        var isValid = await extension.ValidateAsync();
        if (!isValid)
        {
            logger.LogWarning("Extension validation failed: {ExtensionId}", extensionId);
        }
        else
        {
            logger.LogInformation("Extension ready: {ExtensionId}", extensionId);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize extension: {ExtensionId}",
            extension.GetManifest().Metadata.Id);
    }
}

// Map all endpoints
app.MapAuthEndpoints();
app.MapDatasetEndpoints();
app.MapItemEditEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// Top-level statements compile into a `private` Program class by default, which
// WebApplicationFactory<Program> (Microsoft.AspNetCore.Mvc.Testing) can't target from the
// test assembly. This partial declaration makes it public/reachable for integration tests
// without changing anything about how the app itself runs.
public partial class Program { }
