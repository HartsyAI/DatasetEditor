using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// STANDING REGRESSION TEST — keep this in the suite permanently, through every later
    /// phase. Before Phase 0, a single mapped endpoint (GetAllDatasets, bound to an
    /// unregistered DI interface) made ASP.NET Core's route-table construction throw. That
    /// construction is lazy and happens once for the WHOLE composite EndpointDataSource on
    /// the first request through routing middleware — so the failure wasn't scoped to that
    /// one endpoint, it took down every mapped route in the API, including completely
    /// unrelated ones. This test would have caught it on day one: it proves the route table
    /// itself builds successfully by observing that ANY request — including one for a route
    /// that deliberately doesn't exist — gets a real routing decision (200 or 404) instead
    /// of a 500 from route-table construction blowing up. A new endpoint file added in a
    /// later phase (auth, thumbnails, S3) that reintroduces this bug class will fail here
    /// before it fails anywhere else.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class RouteEnumerationRegressionTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public RouteEnumerationRegressionTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        [Fact]
        public async Task UnmatchedRoute_Returns404_NotA500FromRouteTableConstruction()
        {
            using HttpClient client = _factory.CreateClient();

            // Program.cs maps a SPA fallback (MapFallbackToFile("index.html")) that matches
            // any path WITHOUT a file extension, so a plain unmatched path like "/nope"
            // legitimately 200s with the Blazor host page — that's correct SPA behavior, not
            // a bug. A path with a fake extension skips the fallback's "{*path:nonfile}"
            // pattern and exercises real routing's "no match" outcome instead.
            HttpResponseMessage response = await client.GetAsync("/this-route-does-not-exist-anywhere.test");

            // A 500 here means route-table construction itself is throwing (the original
            // bug), not that this particular path is unmapped — a real "not found" is a 404.
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Theory]
        [InlineData("GET", "/api/datasets")]
        [InlineData("GET", "/api/datasets/00000000-0000-0000-0000-000000000000")]
        [InlineData("GET", "/api/datasets/00000000-0000-0000-0000-000000000000/items")]
        [InlineData("GET", "/api/datasets/00000000-0000-0000-0000-000000000000/files/x.jpg")]
        [InlineData("DELETE", "/api/datasets/00000000-0000-0000-0000-000000000000")]
        public async Task EveryMappedDatasetRoute_NeverReturns500(string method, string path)
        {
            using HttpClient client = _factory.CreateClient();
            using HttpRequestMessage request = new(new HttpMethod(method), path);

            HttpResponseMessage response = await client.SendAsync(request);

            // These all hit an unknown/empty dataset, so 404 (or 400 for a malformed
            // request) is the correct outcome — the point is proving none of them 500,
            // which is what "the route table never finished building" looks like.
            ((int)response.StatusCode).Should().BeLessThan(500,
                $"{method} {path} returned {(int)response.StatusCode} — endpoint metadata may be broken again");
        }
    }
}
