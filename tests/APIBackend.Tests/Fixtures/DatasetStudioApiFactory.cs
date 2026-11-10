using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DatasetStudio.Tests.APIBackend.Fixtures
{
    /// <summary>
    /// Boots the real API (via WebApplicationFactory&lt;Program&gt; — Program.cs's top-level
    /// statements need the `public partial class Program {}` marker at the end of the file
    /// for this to be able to target it) against its own uniquely-named database inside the
    /// shared <see cref="PostgresContainerFixture"/> Postgres instance, so tests get real
    /// startup behavior (config binding, migrations, extension discovery, DI wiring) rather
    /// than a hand-built service collection that could silently diverge from production.
    /// One instance = one throwaway database, created on init and dropped on dispose, so
    /// tests in different classes never see each other's data even though they share the
    /// same underlying Postgres container.
    /// </summary>
    public sealed class DatasetStudioApiFactory : WebApplicationFactory<Program>
    {
        private const string ConnectionStringEnvVar = "ConnectionStrings__DatasetStudio";
        private const string DatasetRootPathEnvVar = "Storage__DatasetRootPath";
        private const string ThumbnailPathEnvVar = "Storage__ThumbnailPath";
        private const string ParquetPathEnvVar = "Storage__ParquetPath";
        private const string UploadPathEnvVar = "Storage__UploadPath";
        private const string ExtensionsBuiltInPathEnvVar = "Extensions__BuiltInPath";

        private readonly string _baseConnectionString;
        private readonly string _databaseName;
        private readonly string? _previousConnectionStringEnvVar;
        private readonly string? _previousDatasetRootPathEnvVar;
        private readonly string? _previousThumbnailPathEnvVar;
        private readonly string? _previousParquetPathEnvVar;
        private readonly string? _previousUploadPathEnvVar;
        private readonly string? _previousExtensionsBuiltInPathEnvVar;
        /// <summary>The database this host uses (pass it to another factory to simulate a restart).</summary>
        public string DatabaseName => _databaseName;

        private string DatabaseConnectionString =>
            new NpgsqlConnectionStringBuilder(_baseConnectionString) { Database = _databaseName }.ConnectionString;

        /// <summary>This factory's isolated dataset-file storage root (Storage:DatasetRootPath).</summary>
        public string DatasetRootPath { get; } =
            Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "datasets", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// This factory's isolated thumbnail cache root (Storage:ThumbnailPath). Without this
        /// override, every test process writes into the repo's real ./blobs/thumbnails and a
        /// stale cache entry from one test run can make a later test's "was this actually
        /// generated" assertion pass for the wrong reason.
        /// </summary>
        public string ThumbnailPath { get; } =
            Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "thumbnails", Guid.NewGuid().ToString("N"));

        /// <summary>This factory's isolated Parquet item store (Storage:ParquetPath).</summary>
        public string ParquetPath { get; } =
            Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "parquet", Guid.NewGuid().ToString("N"));

        /// <summary>This factory's isolated upload staging root (Storage:UploadPath).</summary>
        public string UploadPath { get; } =
            Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "uploads", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Optional test-supplied DI overrides (e.g. swapping IHuggingFaceDatasetServerClient
        /// for a mock so a hf-thumbnail test never makes a real network call). Must be set
        /// before the first CreateClient()/Services access, same timing constraint as the
        /// constructor's env var overrides above -- WebApplicationFactory builds the host lazily
        /// on first use and ConfigureWebHost only runs as part of that build.
        /// </summary>
        public Action<IServiceCollection>? ConfigureTestServices { get; set; }

        /// <summary>
        /// Server folders this host may import from (Storage:FolderImportRoots). Defaults to the
        /// shared test temp tree, where the folder-import tests create their source folders; set
        /// it to an empty array to test the "folder import disabled" default. Same timing
        /// constraint as <see cref="ConfigureTestServices"/>.
        /// </summary>
        public string[] FolderImportRoots { get; set; } = { Path.Combine(Path.GetTempPath(), "DatasetStudioTests") };

        /// <summary>
        /// Extra configuration values for settings the API reads after startup (per request or
        /// when a service is first resolved), e.g. <c>RateLimiting:S3FilePermitsPerMinute</c>.
        /// Added after every other source, so they win. Same timing constraint as
        /// <see cref="ConfigureTestServices"/>.
        /// </summary>
        public Dictionary<string, string?> ConfigOverrides { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                Dictionary<string, string?> values = new(ConfigOverrides);
                for (int i = 0; i < FolderImportRoots.Length; i++)
                {
                    values[$"Storage:FolderImportRoots:{i}"] = FolderImportRoots[i];
                }
                configurationBuilder.AddInMemoryCollection(values);
            });

            // Test classes register and log in many users from one (in-process) client address,
            // which the production per-IP limits would reject. Registered before the test's own
            // overrides so a rate-limit test can lower them again.
            builder.ConfigureServices(services => services.Configure<global::DatasetStudio.APIBackend.Services.Auth.AuthOptions>(options =>
            {
                options.AllowRegistration = true;
                options.LoginAttemptsPerMinute = 100_000;
                options.RegistrationsPerHour = 100_000;
            }));

            if (ConfigureTestServices is not null)
            {
                builder.ConfigureServices(ConfigureTestServices);
            }
        }

        /// <param name="baseConnectionString">Connection string of the shared Postgres container.</param>
        /// <param name="databaseName">An existing database to reuse (restart tests); a new one otherwise.</param>
        /// <param name="extensionsBuiltInPath">
        /// Overrides Extensions:BuiltInPath. Extensions are discovered before Build(), so like the
        /// connection string this must be an environment variable set before the host is built.
        /// Null keeps the default folder (src/Extensions/BuiltIn, i.e. CoreViewer).
        /// </param>
        public DatasetStudioApiFactory(string baseConnectionString, string? databaseName = null, string? extensionsBuiltInPath = null)
        {
            // A caller passes an existing database name to start a second host on the same data,
            // which is how the restart tests simulate a process restart.
            _databaseName = databaseName ?? $"dataset_studio_test_{Guid.NewGuid():N}";
            _baseConnectionString = baseConnectionString;

            // Program.cs reads ConnectionStrings:DatasetStudio synchronously, into a closure,
            // the moment AddDatasetServices runs as top-level code — long before
            // WebApplicationFactory's ConfigureWebHost/ConfigureAppConfiguration hooks get a
            // chance to run (those apply around the later Build() call). No configuration
            // source added through the normal WebApplicationFactory override APIs can win
            // against a value that's already been read and baked in by the time they fire.
            // An environment variable, set here before the host is ever constructed (lazily,
            // on first CreateClient()/Services access below), is read inline by Program.cs's
            // own AddEnvironmentVariables() call — which happens BEFORE AddDatasetServices in
            // the same synchronous script — so it's visible in time. Restored in Dispose so
            // sequential tests (or DATASETSTUDIO_TEST_POSTGRES_CONNECTION-based real-Postgres
            // runs) don't leak this across factory instances.
            _previousConnectionStringEnvVar = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, DatabaseConnectionString);

            // Storage:DatasetRootPath IS read fresh per-request (unlike the connection
            // string), so this override doesn't strictly need the same "before host
            // construction" timing — but setting it here keeps both overrides in one place.
            _previousDatasetRootPathEnvVar = Environment.GetEnvironmentVariable(DatasetRootPathEnvVar);
            Environment.SetEnvironmentVariable(DatasetRootPathEnvVar, DatasetRootPath);

            _previousThumbnailPathEnvVar = Environment.GetEnvironmentVariable(ThumbnailPathEnvVar);
            Environment.SetEnvironmentVariable(ThumbnailPathEnvVar, ThumbnailPath);

            // Parquet and upload roots are read once at startup (AddDatasetServices), so they must be
            // set here, before the host is built, like the connection string. Without these overrides
            // local-dataset tests write into the shared ./data and ./uploads of the test output folder.
            _previousParquetPathEnvVar = Environment.GetEnvironmentVariable(ParquetPathEnvVar);
            Environment.SetEnvironmentVariable(ParquetPathEnvVar, ParquetPath);

            _previousUploadPathEnvVar = Environment.GetEnvironmentVariable(UploadPathEnvVar);
            Environment.SetEnvironmentVariable(UploadPathEnvVar, UploadPath);

            _previousExtensionsBuiltInPathEnvVar = Environment.GetEnvironmentVariable(ExtensionsBuiltInPathEnvVar);
            Environment.SetEnvironmentVariable(ExtensionsBuiltInPathEnvVar, extensionsBuiltInPath);
        }

        /// <summary>Creates this factory's throwaway database. Call once before the first request.</summary>
        public async Task InitializeDatabaseAsync()
        {
            NpgsqlConnectionStringBuilder adminBuilder = new(_baseConnectionString) { Database = "postgres", Pooling = false };
            await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
            await adminConnection.OpenAsync();
            await using NpgsqlCommand createDb = new($"CREATE DATABASE \"{_databaseName}\"", adminConnection);
            await createDb.ExecuteNonQueryAsync();
        }

        /// <summary>Drops this factory's throwaway database. Call once after the last request.</summary>
        public async Task DropDatabaseAsync()
        {
            NpgsqlConnection.ClearAllPools();
            NpgsqlConnectionStringBuilder adminBuilder = new(_baseConnectionString) { Database = "postgres", Pooling = false };
            await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
            await adminConnection.OpenAsync();
            await using (NpgsqlCommand terminate = new(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName AND pid <> pg_backend_pid();",
                adminConnection))
            {
                terminate.Parameters.AddWithValue("dbName", _databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            // WITH (FORCE) because a background service (ingestion polling, the thumbnail sweep) can
            // open a new connection between the terminate above and the drop, which failed the drop
            // with 55006 "being accessed by other users" and the test with it.
            await using NpgsqlCommand dropDb = new($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", adminConnection);
            await dropDb.ExecuteNonQueryAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _previousConnectionStringEnvVar);
            Environment.SetEnvironmentVariable(DatasetRootPathEnvVar, _previousDatasetRootPathEnvVar);
            Environment.SetEnvironmentVariable(ThumbnailPathEnvVar, _previousThumbnailPathEnvVar);
            Environment.SetEnvironmentVariable(ParquetPathEnvVar, _previousParquetPathEnvVar);
            Environment.SetEnvironmentVariable(UploadPathEnvVar, _previousUploadPathEnvVar);
            Environment.SetEnvironmentVariable(ExtensionsBuiltInPathEnvVar, _previousExtensionsBuiltInPathEnvVar);
            try
            {
                if (Directory.Exists(DatasetRootPath))
                {
                    Directory.Delete(DatasetRootPath, recursive: true);
                }
                if (Directory.Exists(ThumbnailPath))
                {
                    Directory.Delete(ThumbnailPath, recursive: true);
                }
                if (Directory.Exists(ParquetPath))
                {
                    Directory.Delete(ParquetPath, recursive: true);
                }
                if (Directory.Exists(UploadPath))
                {
                    Directory.Delete(UploadPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a leftover temp dir isn't worth failing test teardown over.
            }
        }
    }
}
