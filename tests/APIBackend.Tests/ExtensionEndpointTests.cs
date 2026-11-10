using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.DTO.Auth;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Runs the real Program.cs with the SampleExtension fixture as the only built-in extension:
    /// discovery, ConfigureServices before Build(), and the authenticated /api/extensions/{id}
    /// route group after.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class ExtensionEndpointTests : IAsyncLifetime
    {
        private const string GreetingRoute = "/api/extensions/sampleextension/greeting";

        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public ExtensionEndpointTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString, extensionsBuiltInPath: ApiExtensionLoaderTests.FixturesPath);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        [Fact]
        public async Task ExtensionRoute_Anonymous_Returns401()
        {
            // The CoreViewer placeholder routes were anonymous (audit X-5). Extension routes now
            // live in a group that requires an authenticated user, whatever the extension maps.
            using HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.GetAsync(GreetingRoute);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ExtensionRoute_SignedIn_RunsTheExtensionWithItsPrivateDependency()
        {
            using HttpClient client = _factory.CreateClient();
            HttpResponseMessage registered = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("extension-user", "extension-user@example.com", "password123"));
            registered.StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage response = await client.GetAsync(GreetingRoute);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Dataset Studio, from SampleExtension.Greeting");
        }
    }
}
