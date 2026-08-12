using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.APIBackend.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatasetServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger startupLogger)
    {
        // ========================================
        // PostgreSQL Database
        // ========================================

        string? connectionString = configuration.GetConnectionString("DatasetStudio");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string 'DatasetStudio' is not configured in appsettings.json");
        }

        services.AddDbContext<DatasetStudioDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);

                npgsqlOptions.MigrationsAssembly(typeof(DatasetStudioDbContext).Assembly.GetName().Name);
            });

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        startupLogger.LogInformation("PostgreSQL configured with connection: {ConnectionString}", MaskConnectionString(connectionString));

        // ========================================
        // Repositories
        // ========================================

        services.AddScoped<Core.Abstractions.Repositories.IDatasetRepository, DatasetRepository>();

        // ========================================
        // Dataset Management Services
        // ========================================

        services.AddScoped<IDatasetIngestionService, DatasetIngestionService>();

        // Background ingestion: uploads enqueue a job and return immediately instead of
        // blocking the request thread while a multi-GB file is parsed.
        services.AddSingleton<IngestionQueue>();
        services.AddHostedService<IngestionBackgroundService>();

        // ========================================
        // HuggingFace Integration
        // ========================================

        services.AddHttpClient<IHuggingFaceClient, HuggingFaceClient>();
        services.AddHttpClient<IHuggingFaceDatasetServerClient, HuggingFaceDatasetServerClient>();
        services.AddScoped<IHuggingFaceDiscoveryService, HuggingFaceDiscoveryService>();
        services.AddSingleton<IHuggingFaceRowPageCache, HuggingFaceRowPageCache>();

        // ========================================
        // Storage Directories
        // ========================================

        string parquetPath = configuration["Storage:ParquetPath"] ?? "./data/parquet";
        string blobPath = configuration["Storage:BlobPath"] ?? "./blobs";
        string thumbnailPath = configuration["Storage:ThumbnailPath"] ?? "./blobs/thumbnails";
        string uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";
        string datasetRootPath = configuration["Storage:DatasetRootPath"] ?? "./data/datasets";

        // Register ParquetItemRepository as singleton (handles Parquet I/O)
        services.AddSingleton(serviceProvider =>
        {
            ILogger<ParquetItemRepository> logger = serviceProvider.GetRequiredService<ILogger<ParquetItemRepository>>();
            return new ParquetItemRepository(parquetPath, logger);
        });

        // The dataset/item endpoints and ingestion service depend on the API-layer,
        // cursor-based IDatasetItemRepository. Resolve it from the same Parquet singleton
        // so they share its in-memory count cache and write lock. (Without this the item
        // endpoints fail at runtime: the interface was never registered.)
        services.AddSingleton<Services.DatasetManagement.IDatasetItemRepository>(
            serviceProvider => serviceProvider.GetRequiredService<ParquetItemRepository>());

        // Register ItemRepository as scoped adapter that wraps ParquetItemRepository
        // (Core abstraction used by non-endpoint callers).
        services.AddScoped<Core.Abstractions.Repositories.IDatasetItemRepository, ItemRepository>();

        Directory.CreateDirectory(parquetPath);
        Directory.CreateDirectory(blobPath);
        Directory.CreateDirectory(thumbnailPath);
        Directory.CreateDirectory(uploadPath);
        Directory.CreateDirectory(datasetRootPath);

        // ========================================
        // Thumbnails
        // ========================================

        services.AddSingleton<IThumbnailService>(serviceProvider =>
        {
            ILogger<ThumbnailService> logger = serviceProvider.GetRequiredService<ILogger<ThumbnailService>>();
            return new ThumbnailService(thumbnailPath, logger);
        });

        // ========================================
        // S3 / S3-compatible storage
        // ========================================

        // Same GetSection(...).Get<T[]>() idiom already used for Cors:AllowedOrigins -- real
        // credentials come from environment variables (Storage__S3StorageProfiles__0__AccessKey,
        // etc.) at deploy time, never committed to appsettings.json.
        S3StorageProfileConfig[] s3StorageProfiles =
            configuration.GetSection("Storage:S3StorageProfiles").Get<S3StorageProfileConfig[]>()
            ?? Array.Empty<S3StorageProfileConfig>();
        services.AddSingleton<IS3StorageProfileResolver>(new S3StorageProfileResolver(s3StorageProfiles));
        services.AddSingleton<IS3StorageService, S3StorageService>();
        startupLogger.LogInformation("S3 storage profiles configured: {Profiles}",
            s3StorageProfiles.Length == 0 ? "(none)" : string.Join(", ", s3StorageProfiles.Select(p => p.Name)));

        startupLogger.LogInformation(
            "Storage directories created: Parquet={Parquet} Blobs={Blobs} Thumbnails={Thumbnails} Uploads={Uploads} Datasets={Datasets}",
            parquetPath, blobPath, thumbnailPath, uploadPath, datasetRootPath);

        return services;
    }

    private static string MaskConnectionString(string connectionString)
    {
        // Mask sensitive parts of connection string for logging
        var parts = connectionString.Split(';');
        var masked = parts.Select(part =>
        {
            if (part.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("Pwd=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Split('=')[0] + "=***";
            }
            return part;
        });
        return string.Join(';', masked);
    }
}
