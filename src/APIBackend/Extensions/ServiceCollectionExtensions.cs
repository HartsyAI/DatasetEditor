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
            // Program.cs checks this earlier with the same message; this guard covers any other
            // host that calls AddDatasetServices without that check.
            throw new InvalidOperationException(
                "PostgreSQL connection string 'DatasetStudio' is not configured. Set ConnectionStrings__DatasetStudio.");
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

        services.AddScoped<IDatasetRepository, DatasetRepository>();

        // Edits of streamed (HuggingFace, S3) items, which can't be written back (audit E-3).
        services.AddScoped<IItemOverlayRepository, ItemOverlayRepository>();

        // ========================================
        // Dataset Management Services
        // ========================================

        services.AddScoped<IDatasetIngestionService, DatasetIngestionService>();

        // Background ingestion: imports store a job in ingestion_jobs and return immediately
        // instead of blocking the request thread while a multi-GB file is parsed. The queue is
        // persistent so a restart doesn't lose work (audit I-1).
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.AddSingleton<IngestionSignal>();
        services.AddScoped<IngestionJobStore>();
        services.AddScoped<IIngestionJobQueue>(sp => sp.GetRequiredService<IngestionJobStore>());
        services.AddScoped<IIngestionJobStore>(sp => sp.GetRequiredService<IngestionJobStore>());
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

        // The item endpoints, ingestion and the ingestion worker all depend on
        // IDatasetItemRepository. Resolve it from the same Parquet singleton so every caller
        // shares its per-dataset locks. (There used to be a second, Core-side interface reached
        // through a scoped pass-through adapter; audit K-4.)
        services.AddSingleton<Services.DatasetManagement.IDatasetItemRepository>(
            serviceProvider => serviceProvider.GetRequiredService<ParquetItemRepository>());

        Directory.CreateDirectory(parquetPath);
        Directory.CreateDirectory(blobPath);
        Directory.CreateDirectory(thumbnailPath);
        Directory.CreateDirectory(uploadPath);
        Directory.CreateDirectory(datasetRootPath);

        // ========================================
        // Thumbnails
        // ========================================

        ThumbnailOptions thumbnailOptions = new();
        configuration.GetSection(ThumbnailOptions.SectionName).Bind(thumbnailOptions);
        services.AddSingleton<IThumbnailService>(serviceProvider =>
        {
            ILogger<ThumbnailService> logger = serviceProvider.GetRequiredService<ILogger<ThumbnailService>>();
            return new ThumbnailService(thumbnailPath, logger, thumbnailOptions);
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
