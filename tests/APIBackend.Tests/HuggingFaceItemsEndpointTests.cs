using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Items endpoint behaviour for HuggingFace streaming datasets (audit H-1 and H-6):
    /// the access token reaches the /size auto-discovery, the total is back-filled from /rows, and
    /// an upstream failure is returned as an error instead of an empty 200 page.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class HuggingFaceItemsEndpointTests : IAsyncLifetime
    {
        private const string Repo = "fake/dataset";
        private const string Token = "hf_test_token";

        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private readonly Mock<IHuggingFaceDatasetServerClient> _hfServerClientMock = new();

        public HuggingFaceItemsEndpointTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigureTestServices = services => services.AddSingleton(_hfServerClientMock.Object);

            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();

            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("hf-items-test-user", "hf-items-test-user@example.com", "password123"));
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        /// <summary>Creates a public streaming dataset with the given split and total, bypassing ingestion.</summary>
        private async Task<DatasetDetailDto> CreateStreamingDatasetAsync(string? split, long totalItems = 0)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("hf-items-" + Guid.NewGuid().ToString("N")[..8], null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == created.Id);
            entity.SourceType = DatasetSourceType.HuggingFaceStreaming;
            entity.IsStreaming = true;
            entity.HuggingFaceRepository = Repo;
            entity.HuggingFaceSplit = split;
            entity.IsPublic = true;
            entity.TotalItems = totalItems;
            await db.SaveChangesAsync();

            return created;
        }

        private void SetupRows(long numRowsTotal)
        {
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceRowsPage
                {
                    Dataset = Repo,
                    Config = "default",
                    Split = "train",
                    NumRowsTotal = numRowsTotal,
                    Rows = new List<HuggingFaceRow>(),
                });
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
        public async Task GetItems_UpstreamFailure_ReturnsErrorInsteadOfEmptyPage(HttpStatusCode upstream, HttpStatusCode expected)
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train");
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HuggingFaceUpstreamException("upstream failed", upstream));

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");

            response.StatusCode.Should().Be(expected);
        }

        [Fact]
        public async Task GetItems_WithoutSplit_PassesAccessTokenToSizeDiscovery()
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: null);
            _hfServerClientMock
                .Setup(c => c.GetDatasetSizeAsync(Repo, null, null, Token, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceDatasetSizeInfo { Dataset = Repo, Config = "default", Split = "train", NumRows = 500 });
            SetupRows(500);

            using HttpRequestMessage request = new(HttpMethod.Get, $"/api/datasets/{dataset.Id}/items?pageSize=10");
            request.Headers.Add("X-HF-Access-Token", Token);
            HttpResponseMessage response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _hfServerClientMock.Verify(c => c.GetDatasetSizeAsync(Repo, null, null, Token, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetItems_TotalUnknown_BackfillsTotalFromRowsTotal()
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train", totalItems: 0);
            SetupRows(numRowsTotal: 70_000);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            long stored = (await db.Datasets.AsNoTracking().FirstAsync(d => d.Id == dataset.Id)).TotalItems;
            stored.Should().Be(70_000);
        }

        [Fact]
        public async Task GetItems_SizeDiscoveryFails_StillServesRowsFromTrainSplit()
        {
            // Codex P2 on PR #7: /size is best-effort. A failed discovery must not block a dataset whose
            // /rows can still be read; only a failed rows request is an error.
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: null);
            _hfServerClientMock
                .Setup(c => c.GetDatasetSizeAsync(Repo, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HuggingFaceUpstreamException("size unavailable", HttpStatusCode.NotFound));
            SetupRows(numRowsTotal: 42);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _hfServerClientMock.Verify(c => c.GetRowsAsync(Repo, It.IsAny<string?>(), "train", 0, 10, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("1.5")]
        public async Task GetItems_MalformedStreamingCursor_Returns400BeforeAnyUpstreamCall(string cursor)
        {
            // Audit G-3: a malformed offset used to restart at row 0 silently. It is rejected up front,
            // before /size discovery or /rows, so no upstream request is made for it.
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train");

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/items?pageSize=10&cursor={Uri.EscapeDataString(cursor)}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            _hfServerClientMock.Verify(
                c => c.GetRowsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>Audit H-8: streamed items never had DatasetId set, unlike every other source.</summary>
        [Fact]
        public async Task GetItems_StreamingRows_CarryTheirDatasetId()
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train");
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceRowsPage
                {
                    Dataset = Repo,
                    Config = "default",
                    Split = "train",
                    NumRowsTotal = 1,
                    Rows = new List<HuggingFaceRow>
                    {
                        new()
                        {
                            RowIndex = 0,
                            Columns = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["text"] = System.Text.Json.JsonSerializer.SerializeToElement("a caption"),
                            },
                        },
                    },
                });

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");
            DatasetStudio.DTO.Common.PageResponse<DatasetItemDto>? page =
                await response.Content.ReadFromJsonAsync<DatasetStudio.DTO.Common.PageResponse<DatasetItemDto>>();

            page!.Items.Should().ContainSingle().Which.DatasetId.Should().Be(dataset.Id);
        }
    }
}
