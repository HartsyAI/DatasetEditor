using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace DatasetStudio.Tests.APIBackend.Fixtures
{
    /// <summary>
    /// Starts one Postgres container for the whole "Postgres collection" (see
    /// <see cref="PostgresCollection"/>), so integration tests get a real Postgres —
    /// catching jsonb/enum-mapping bugs the EF InMemory provider can't — without every
    /// test class paying its own container-startup cost.
    /// DATASETSTUDIO_TEST_POSTGRES_CONNECTION, if set, overrides this (e.g. to point at a
    /// long-lived dev Postgres instead of spinning up a container).
    /// </summary>
    public sealed class PostgresContainerFixture : IAsyncLifetime
    {
        private PostgreSqlContainer? _container;

        public string ConnectionString { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            string? overrideConnectionString =
                System.Environment.GetEnvironmentVariable("DATASETSTUDIO_TEST_POSTGRES_CONNECTION");
            if (!string.IsNullOrWhiteSpace(overrideConnectionString))
            {
                ConnectionString = overrideConnectionString;
                return;
            }

            _container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("dataset_studio_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Shares one Postgres container across every Docker-backed class. S3 tests attach their own
    /// <see cref="S3ContainerFixture"/> per class instead of joining this collection, so an S3
    /// container failure cannot take down the rest of the suite (audit Q-2).
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
    {
        public const string Name = "Postgres collection";
    }
}
