using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL
{
    /// <summary>
    /// Design-time factory for DatasetStudioDbContext so that `dotnet ef` can create
    /// the DbContext without relying on the full web host or other services.
    /// </summary>
    /// <remarks>
    /// Reads the same configuration sources as Program.cs (Configuration/appsettings.json, its
    /// environment overlay, then environment variables). Before this, it read a second, stale pair
    /// of appsettings files at the project root that the running API never loaded (audit C-2).
    /// The environment defaults to Development because that is the only environment whose overlay
    /// carries a local database connection string. Set ASPNETCORE_ENVIRONMENT to override.
    /// </remarks>
    public sealed class DatasetStudioDbContextFactory : IDesignTimeDbContextFactory<DatasetStudioDbContext>
    {
        /// <summary>
        /// Builds a context from the API's configuration. Works whether `dotnet ef` runs from the
        /// repository root or from the API project folder.
        /// </summary>
        /// <param name="args">Arguments passed by the EF tooling; not used.</param>
        /// <returns>A context bound to the configured PostgreSQL database.</returns>
        public DatasetStudioDbContext CreateDbContext(string[] args)
        {
            string apiDirectory = ResolveApiDirectory();
            string environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Development";

            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(apiDirectory)
                .AddJsonFile(Path.Combine("Configuration", "appsettings.json"), optional: true)
                .AddJsonFile(Path.Combine("Configuration", $"appsettings.{environmentName}.json"), optional: true)
                .AddEnvironmentVariables()
                .Build();

            string? connectionString = configuration.GetConnectionString("DatasetStudio");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DatasetStudio' is not configured. Set ConnectionStrings__DatasetStudio " +
                    "or use the Development environment, which provides the local database connection string.");
            }

            DbContextOptionsBuilder<DatasetStudioDbContext> builder = new DbContextOptionsBuilder<DatasetStudioDbContext>();
            builder.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(DatasetStudioDbContext).Assembly.GetName().Name);
            });

            DatasetStudioDbContext context = new DatasetStudioDbContext(builder.Options);
            return context;
        }

        /// <summary>
        /// Finds the API project folder: the current directory when `dotnet ef` runs inside it, or
        /// src/APIBackend when it runs from the repository root.
        /// </summary>
        /// <returns>The directory that holds Configuration/appsettings.json.</returns>
        private static string ResolveApiDirectory()
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            string[] candidates = { currentDirectory, Path.Combine(currentDirectory, "src", "APIBackend") };

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "Configuration", "appsettings.json")))
                {
                    return candidate;
                }
            }

            return currentDirectory;
        }
    }
}
