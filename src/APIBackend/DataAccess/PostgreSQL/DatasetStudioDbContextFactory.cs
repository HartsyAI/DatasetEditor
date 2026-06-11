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
    public sealed class DatasetStudioDbContextFactory : IDesignTimeDbContextFactory<DatasetStudioDbContext>
    {
        public DatasetStudioDbContext CreateDbContext(string[] args)
        {
            string basePath = Directory.GetCurrentDirectory();

            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddJsonFile(Path.Combine("Configuration", "appsettings.json"), optional: true)
                .AddJsonFile(Path.Combine("Configuration", "appsettings.Development.json"), optional: true)
                .AddEnvironmentVariables();

            IConfigurationRoot configuration = configurationBuilder.Build();

            string? connectionString = configuration.GetConnectionString("DatasetStudio");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'DatasetStudio' is not configured.");
            }

            DbContextOptionsBuilder<DatasetStudioDbContext> builder = new DbContextOptionsBuilder<DatasetStudioDbContext>();
            builder.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(DatasetStudioDbContext).Assembly.GetName().Name);
            });

            DatasetStudioDbContext context = new DatasetStudioDbContext(builder.Options);
            return context;
        }
    }
}
