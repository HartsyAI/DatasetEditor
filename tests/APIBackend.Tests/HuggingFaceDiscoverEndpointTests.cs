using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit H-2: <c>/api/datasets/huggingface/discover</c> was anonymous and unthrottled, so anyone
    /// could make the server run several upstream Hub calls (with any token they supplied) per
    /// request. It now requires a signed-in user and has a per-caller rate limit.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class HuggingFaceDiscoverEndpointTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private readonly Mock<IHuggingFaceDiscoveryService> _discovery = new();
        private DatasetStudioApiFactory _factory = default!;

        public HuggingFaceDiscoverEndpointTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _discovery
                .Setup(d => d.DiscoverDatasetAsync(It.IsAny<HuggingFaceDiscoveryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceDiscoveryResponse { Repository = "org/ds", IsAccessible = true });

            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigOverrides["RateLimiting:HuggingFaceDiscoverPermitsPerMinute"] = "2";
            _factory.ConfigureTestServices = services => services.AddScoped(_ => _discovery.Object);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        private static Task<HttpResponseMessage> DiscoverAsync(HttpClient client)
        {
            return client.PostAsJsonAsync("/api/datasets/huggingface/discover", new HuggingFaceDiscoveryRequest { Repository = "org/ds" });
        }

        [Fact]
        public async Task Discover_Anonymous_Returns401WithoutCallingUpstream()
        {
            using HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await DiscoverAsync(client);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            _discovery.Verify(d => d.DiscoverDatasetAsync(It.IsAny<HuggingFaceDiscoveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Discover_SignedIn_IsRateLimitedPerCaller()
        {
            using HttpClient client = _factory.CreateClient();
            await client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("discover-user", "discover-user@example.com", "password123"));

            (await DiscoverAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await DiscoverAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);
            HttpResponseMessage limited = await DiscoverAsync(client);

            limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            limited.Headers.Contains("Retry-After").Should().BeTrue();
        }
    }
}
