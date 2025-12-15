using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.APIBackend.Services.Storage;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatasetServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
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

        Logs.Info($"PostgreSQL configured with connection: {MaskConnectionString(connectionString)}");

        // ========================================
        // Storage Services
        // ========================================

        // Parquet service for dataset item storage
        services.AddSingleton<IParquetDataService, ParquetDataService>();

        // ========================================
        // Repositories
        // ========================================

        services.AddScoped<IDatasetRepository, DatasetRepository>();

        // ========================================
        // Dataset Management Services
        // ========================================

        services.AddSingleton<IDatasetIngestionService, NoOpDatasetIngestionService>();

        // ========================================
        // HuggingFace Integration
        // ========================================

        services.AddHttpClient<IHuggingFaceClient, HuggingFaceClient>();
        services.AddHttpClient<IHuggingFaceDatasetServerClient, HuggingFaceDatasetServerClient>();
        services.AddScoped<IHuggingFaceDiscoveryService, HuggingFaceDiscoveryService>();

        // ========================================
        // Storage Directories
        // ========================================

        string parquetPath = configuration["Storage:ParquetPath"] ?? "./data/parquet";
        string blobPath = configuration["Storage:BlobPath"] ?? "./blobs";
        string thumbnailPath = configuration["Storage:ThumbnailPath"] ?? "./blobs/thumbnails";
        string uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";
        string datasetRootPath = configuration["Storage:DatasetRootPath"] ?? "./data/datasets";

        services.AddSingleton<IDatasetItemRepository>(serviceProvider =>
        {
            ILogger<ParquetItemRepository> logger = serviceProvider.GetRequiredService<ILogger<ParquetItemRepository>>();
            return new ParquetItemRepository(parquetPath, logger);
        });

        Directory.CreateDirectory(parquetPath);
        Directory.CreateDirectory(blobPath);
        Directory.CreateDirectory(thumbnailPath);
        Directory.CreateDirectory(uploadPath);
        Directory.CreateDirectory(datasetRootPath);

        Logs.Info($"Storage directories created:");
        Logs.Info($"  Parquet: {parquetPath}");
        Logs.Info($"  Blobs: {blobPath}");
        Logs.Info($"  Thumbnails: {thumbnailPath}");
        Logs.Info($"  Uploads: {uploadPath}");
        Logs.Info($"  Datasets: {datasetRootPath}");

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
