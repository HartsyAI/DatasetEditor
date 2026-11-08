using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace DatasetStudio.Tests.APIBackend.Fixtures
{
    /// <summary>
    /// Starts one S3-compatible container (Adobe S3Mock) for the S3 endpoint tests.
    /// Replaces the MinIO fixture: <c>minio/minio</c> is no longer anonymously pullable from
    /// Docker Hub or quay.io (audit R-5/Q-2), which made every Docker-backed test fail at fixture
    /// start. S3Mock is pullable, pinned to a version tag, and speaks the same path-style S3 API
    /// the app's <c>S3StorageService</c> uses. It ignores credentials, so the test values still
    /// flow through the real AWS SDK code path.
    /// </summary>
    /// <remarks>
    /// Not part of <see cref="PostgresCollection"/>: only <c>S3EndpointsTests</c> uses it, and it
    /// is attached there with <c>IClassFixture</c>. A failure here therefore affects that one
    /// class, not the rest of the Docker-backed suite.
    /// </remarks>
    public sealed class S3ContainerFixture : IAsyncLifetime
    {
        /// <summary>Pinned image. Bump deliberately; do not switch to an untagged reference.</summary>
        public const string Image = "adobe/s3mock:3.12.0";

        /// <summary>Container-internal HTTP port S3Mock listens on.</summary>
        private const int HttpPort = 9090;

        private readonly IContainer _container = new ContainerBuilder()
            .WithImage(Image)
            .WithPortBinding(HttpPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(HttpPort))
            .Build();

        /// <summary>Path-style endpoint URL reachable from the test host.</summary>
        public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}";

        /// <summary>Ignored by S3Mock; present so the AWS SDK has credentials to sign with.</summary>
        public string AccessKey => "test-access-key";

        /// <summary>Ignored by S3Mock; see <see cref="AccessKey"/>.</summary>
        public string SecretKey => "test-secret-key";

        /// <summary>Starts the container before the first test in the class runs.</summary>
        public async Task InitializeAsync()
        {
            await _container.StartAsync();
        }

        /// <summary>Disposes the container after the class's last test.</summary>
        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }
    }
}
