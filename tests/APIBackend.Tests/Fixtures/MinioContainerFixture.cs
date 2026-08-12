using System.Threading.Tasks;
using Testcontainers.Minio;
using Xunit;

namespace DatasetStudio.Tests.APIBackend.Fixtures
{
    /// <summary>
    /// Starts one MinIO container, shared across the whole "Postgres collection" (see
    /// <see cref="PostgresCollection"/>) alongside the one shared Postgres container --
    /// deliberately folded into the SAME collection rather than a separate one. A second
    /// xUnit collection means a second, fully independent Postgres container running
    /// concurrently with the first (collections parallelize by default), which pushed
    /// concurrent EF migration load past a real Npgsql/Postgres concurrency ceiling in
    /// practice (duplicate-key errors on pg_type_typname_nsp_index, "relation already exists")
    /// -- confirmed by moving S3EndpointsTests here and re-running the full suite clean.
    /// </summary>
    public sealed class MinioContainerFixture : IAsyncLifetime
    {
#pragma warning disable CS0618 // MinioBuilder's parameterless constructor is obsolete in favor
        // of an explicit-image overload; not pinning a specific image tag here is a deliberate,
        // low-risk choice for a test-only fixture (worst case: a future package bump changes
        // the default image, caught immediately by this fixture's own tests failing).
        private readonly MinioContainer _container = new MinioBuilder().Build();
#pragma warning restore CS0618

        public string ServiceUrl => _container.GetConnectionString();
        public string AccessKey => _container.GetAccessKey();
        public string SecretKey => _container.GetSecretKey();

        public async Task InitializeAsync()
        {
            await _container.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }
    }

}
