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

            // Phase 3 gated CreateDataset (and everything downstream of an existing dataset --
            // import-huggingface, upload, delete) behind authentication. WebApplicationFactory
            // clients carry cookies by default (HandleCookies defaults to true), so registering
            // once here and reusing _client authenticates every request below it for the rest
            // of this class -- the two purely-anonymous-friendly tests (empty listing, 404 on
            // an unknown id) still behave identically whether the caller happens to be logged
            // in or not.
            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("dataset-endpoints-test-user", "dataset-endpoints-test-user@example.com", "password123"));
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

        // Regression test for audit D-5: create accepted any SourceType and stored it, although
        // only an upload starts from a bare create. WebUrl and LocalFolder had no processing path
        // at all, and a dataset created as HuggingFaceStreaming or ExternalS3Streaming had none of
        // the repository/bucket fields its items endpoint reads. The import endpoints set those
        // source types themselves.
        [Theory]
        [InlineData(DatasetSourceType.WebUrl)]
        [InlineData(DatasetSourceType.LocalFolder)]
        [InlineData(DatasetSourceType.HuggingFace)]
        [InlineData(DatasetSourceType.HuggingFaceStreaming)]
        [InlineData(DatasetSourceType.ExternalS3Streaming)]
        public async Task CreateDataset_WithASourceTypeOtherThanUpload_Returns400AndCreatesNothing(DatasetSourceType sourceType)
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("Unsupported source", null, sourceType));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            DatasetsListResponse? body = await (await _client.GetAsync("/api/datasets")).Content.ReadFromJsonAsync<DatasetsListResponse>();
            body!.Datasets.Should().BeEmpty();
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

        // Regression test for the HF-import scope-capture bug: ImportFromHuggingFace used to
        // do `_ = Task.Run(() => scopedIngestionService.ImportFromHuggingFaceAsync(...))`,
        // racing the request's DI scope (and its scoped DbContext) being disposed right after
        // the handler returns 202. Streaming mode reaches a real DbContext write
        // (UpdateStatusAsync, twice) without any network call, so this deterministically
        // exercises the fixed code path (routed through the ingestion_jobs queue and
        // IngestionBackgroundService, each job getting its own scope) without depending on
        // HuggingFace being reachable from the test environment.
        [Fact]
        public async Task ImportFromHuggingFace_StreamingMode_CompletesWithoutObjectDisposedException()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("HF Streaming Test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            ImportHuggingFaceDatasetRequest importRequest = new()
            {
                Repository = "does-not-need-to-exist/for-streaming-mode",
                IsStreaming = true,
            };

            HttpResponseMessage importResponse = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-huggingface", importRequest);
            importResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            IngestionStatusDto finalStatus = IngestionStatusDto.Pending;
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(200);
                HttpResponseMessage pollResponse = await _client.GetAsync($"/api/datasets/{created.Id}");
                DatasetDetailDto polled = (await pollResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
                finalStatus = polled.Status;
                if (finalStatus is IngestionStatusDto.Completed or IngestionStatusDto.Failed)
                {
                    break;
                }
            }

            // The scope-capture bug would either leave the dataset stuck on Pending/Processing
            // forever (the background exception never got recorded because the DbContext used
            // to record it was itself the thing throwing ObjectDisposedException) or crash the
            // background service. Completed is the correct outcome for streaming mode.
            finalStatus.Should().Be(IngestionStatusDto.Completed);
        }

        private sealed record DatasetsListResponse(
            System.Collections.Generic.List<DatasetSummaryDto> Datasets,
            long TotalCount,
            int Page,
            int PageSize);
    }
}
