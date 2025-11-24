using HartsysDatasetEditor.Api.Repositories;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Core.Utilities;
using LiteDB;

namespace HartsysDatasetEditor.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatasetServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDatasetIngestionService, NoOpDatasetIngestionService>();

        // Register HuggingFace client with HttpClient
        services.AddHttpClient<IHuggingFaceClient, HuggingFaceClient>();

        // Configure LiteDB for persistence
        string dbPath = configuration["Database:LiteDbPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "hartsy.db");

        string? dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        // Register shared LiteDatabase instance (critical: only one instance per file)
        services.AddSingleton<LiteDatabase>(sp =>
        {
            LiteDatabase db = new LiteDatabase(dbPath);
            Logs.Info($"LiteDB initialized at: {dbPath}");
            return db;
        });

        // Register API persistence repositories
        services.AddSingleton<IDatasetRepository, LiteDbDatasetEntityRepository>();
        services.AddSingleton<IDatasetItemRepository, LiteDbDatasetItemRepository>();

        // Create storage directories
        string blobPath = configuration["Storage:BlobPath"] ?? "./blobs";
        string thumbnailPath = configuration["Storage:ThumbnailPath"] ?? "./blobs/thumbnails";
        string uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";
        string datasetRootPath = configuration["Storage:DatasetRootPath"] ?? "./data/datasets";

        Directory.CreateDirectory(blobPath);
        Directory.CreateDirectory(thumbnailPath);
        Directory.CreateDirectory(uploadPath);
        Directory.CreateDirectory(datasetRootPath);

        Logs.Info($"Storage directories created: {blobPath}, {thumbnailPath}, {uploadPath}, {datasetRootPath}");

        // Register background service that can scan dataset folders on disk at startup
        services.AddHostedService<DatasetDiskImportService>();

        return services;
    }
}
