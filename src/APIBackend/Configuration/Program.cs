using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Extensions;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.Extensions.SDK;
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

builder.Services.AddDatasetServices(builder.Configuration, builder.Environment);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }
        policy.AllowAnyHeader().AllowAnyMethod();
    });
});
WebApplication app = builder.Build();

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
app.MapDatasetEndpoints();
app.MapItemEditEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// Top-level statements compile into a `private` Program class by default, which
// WebApplicationFactory<Program> (Microsoft.AspNetCore.Mvc.Testing) can't target from the
// test assembly. This partial declaration makes it public/reachable for integration tests
// without changing anything about how the app itself runs.
public partial class Program { }
