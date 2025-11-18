using HartsysDatasetEditor.Api.Repositories;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Core.Utilities;
using LiteDB;
using CoreInterfaces = HartsysDatasetEditor.Core.Interfaces;

namespace HartsysDatasetEditor.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatasetServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Keep existing API repositories for current API endpoints
        services.AddSingleton<IDatasetRepository, InMemoryDatasetRepository>();
        services.AddSingleton<IDatasetItemRepository, InMemoryDatasetItemRepository>();
        services.AddSingleton<IDatasetIngestionService, NoOpDatasetIngestionService>();

        // Add new LiteDB repositories for enhanced features
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
            var db = new LiteDatabase(dbPath);
            Logs.Info($"LiteDB initialized at: {dbPath}");
            return db;
        });

        // Register Core repositories as singletons (sharing the same database instance)
        services.AddSingleton<CoreInterfaces.IDatasetRepository, LiteDbDatasetRepository>();
        services.AddSingleton<CoreInterfaces.IDatasetItemRepository, LiteDbItemRepository>();

        // Add database initialization service
        services.AddSingleton<DatabaseInitializationService>();

        // Create storage directories
        string blobPath = configuration["Storage:BlobPath"] ?? "./blobs";
        string thumbnailPath = configuration["Storage:ThumbnailPath"] ?? "./blobs/thumbnails";
        string uploadPath = configuration["Storage:UploadPath"] ?? "./uploads";

        Directory.CreateDirectory(blobPath);
        Directory.CreateDirectory(thumbnailPath);
        Directory.CreateDirectory(uploadPath);

        Logs.Info($"Storage directories created: {blobPath}, {thumbnailPath}, {uploadPath}");

        return services;
    }
}
