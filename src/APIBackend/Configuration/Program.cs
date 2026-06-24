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

// Ensure configuration also loads from the Configuration/appsettings*.json files
// where connection strings and storage settings are defined.
builder.Configuration
    .AddJsonFile("Configuration/appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("Configuration/appsettings.Development.json", optional: true, reloadOnChange: true);

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

// Register extension registry as singleton
builder.Services.AddSingleton<ApiExtensionRegistry>();

// Discover extensions (before building the app)
var extensionRegistry = new ApiExtensionRegistry(
    builder.Services.BuildServiceProvider().GetRequiredService<ILogger<ApiExtensionRegistry>>(),
    builder.Configuration,
    builder.Services.BuildServiceProvider());

var extensions = await extensionRegistry.DiscoverAndLoadAsync();

// Configure services for each extension
foreach (var extension in extensions)
{
    try
    {
        extension.ConfigureServices(builder.Services);
    }
    catch (Exception ex)
    {
        var startupLogger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
        startupLogger.LogError(ex, "Failed to configure services for extension: {ExtensionId}",
            extension.GetManifest().Metadata.Id);
    }
}

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
