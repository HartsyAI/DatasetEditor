using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Integration tests against the real, mapped /api/datasets endpoints (via
    /// WebApplicationFactory, so real DI wiring/config binding/route mapping runs, not a
    /// hand-built substitute). Before Phase 0, none of this existed and every one of these
    /// endpoints 500'd on every call — see DatasetStudioApiFactory/PostgresContainerFixture
    /// for the harness these depend on.
    /// One fresh app + throwaway database per test (xUnit creates a new class instance per
    /// [Fact], so IAsyncLifetime here runs per-test), so tests never see each other's data.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class DatasetEndpointsTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;

        public DatasetEndpointsTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        [Fact]
        public async Task GetAllDatasets_OnEmptyDatabase_ReturnsEmptyListNot500()
        {
            HttpResponseMessage response = await _client.GetAsync("/api/datasets");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetsListResponse? body = await response.Content.ReadFromJsonAsync<DatasetsListResponse>();
            body.Should().NotBeNull();
            body!.Datasets.Should().BeEmpty();
            body.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task CreateDataset_ThenGetById_RoundTripsTheDataset()
        {
            CreateDatasetRequest request = new("Test Dataset", "A description", DatasetSourceType.LocalUpload);

            HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/datasets", request);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            DatasetDetailDto? created = await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>();
            created.Should().NotBeNull();
            created!.Name.Should().Be("Test Dataset");
            created.Status.Should().Be(IngestionStatusDto.Pending);

            HttpResponseMessage getResponse = await _client.GetAsync($"/api/datasets/{created.Id}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            DatasetDetailDto? fetched = await getResponse.Content.ReadFromJsonAsync<DatasetDetailDto>();
            fetched.Should().NotBeNull();
            fetched!.Id.Should().Be(created.Id);
            fetched.Name.Should().Be("Test Dataset");
            fetched.Description.Should().Be("A description");
        }

        [Fact]
        public async Task CreateDataset_ThenListAll_IncludesTheNewDataset()
        {
            await _client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("Listed Dataset", null));

            HttpResponseMessage listResponse = await _client.GetAsync("/api/datasets");
            DatasetsListResponse? body = await listResponse.Content.ReadFromJsonAsync<DatasetsListResponse>();

            body.Should().NotBeNull();
            body!.Datasets.Should().ContainSingle(d => d.Name == "Listed Dataset");
            body.TotalCount.Should().Be(1);
        }

        [Fact]
        public async Task DeleteDataset_RemovesItFromSubsequentListing()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("ToDelete", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage deleteResponse = await _client.DeleteAsync($"/api/datasets/{created.Id}");
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            HttpResponseMessage getResponse = await _client.GetAsync($"/api/datasets/{created.Id}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetDataset_ForUnknownId_Returns404NotFound()
        {
            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{Guid.NewGuid()}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        private sealed record DatasetsListResponse(
            System.Collections.Generic.List<DatasetSummaryDto> Datasets,
            long TotalCount,
            int Page,
            int PageSize);
    }
}
