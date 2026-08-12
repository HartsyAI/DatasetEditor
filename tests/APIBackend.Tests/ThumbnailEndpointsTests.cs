using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
using SkiaSharp;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// End-to-end coverage for Phase 4's thumbnail pipeline: ServeDatasetFile's ?w= branch for
    /// locally-stored images, and the hf-thumbnail endpoint for HuggingFace-streaming items.
    /// Real SkiaSharp-encoded bytes throughout, real HTTP pipeline (permission checks, caching),
    /// and a real (mocked) outbound fetch for the hf-thumbnail path so no test hits the network.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class ThumbnailEndpointsTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private readonly Mock<IHuggingFaceDatasetServerClient> _hfServerClientMock = new();

        public ThumbnailEndpointsTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);

            // Must be set before the host is first built (CreateClient()/Services below) --
            // same timing constraint as the constructor's Storage:*Path env var overrides.
            _factory.ConfigureTestServices = services =>
            {
                services.AddSingleton(_hfServerClientMock.Object);

                // hf-thumbnail's outbound asset fetch goes through a NAMED HttpClient
                // (nameof(DatasetEndpoints.GetHuggingFaceThumbnail)); registering that same
                // name here with a fake handler intercepts it without touching the network.
                services.AddHttpClient(nameof(DatasetStudio.APIBackend.Endpoints.DatasetEndpoints.GetHuggingFaceThumbnail))
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeImageHttpMessageHandler());
            };

            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();

            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("thumbnail-test-user", "thumbnail-test-user@example.com", "password123"));
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        private static byte[] MakeTestPng(int width, int height, SKColor color)
        {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(color);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static (int Width, int Height) DecodeDimensions(byte[] bytes)
        {
            using SKBitmap bitmap = SKBitmap.Decode(bytes);
            return (bitmap.Width, bitmap.Height);
        }

        /// <summary>
        /// Places a real, decodable image directly in a dataset's on-disk folder, mirroring
        /// ServeDatasetFileSecurityTests' folder-name convention, without running the full
        /// ingestion pipeline (which this test doesn't need to exercise).
        /// </summary>
        private void PlantImageFile(Guid datasetId, string datasetName, string fileName, byte[] bytes)
        {
            string shortId = datasetId.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"{datasetName}-{shortId}");
            Directory.CreateDirectory(datasetFolder);
            File.WriteAllBytes(Path.Combine(datasetFolder, fileName), bytes);
        }

        [Fact]
        public async Task ServeDatasetFile_WithWidthQuery_ReturnsResizedJpegBoundedToRequestedWidth()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("thumb-local-test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            PlantImageFile(created.Id, "thumb-local-test", "photo.png", MakeTestPng(400, 200, SKColors.CornflowerBlue));

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.png?w=100");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            (int width, int height) = DecodeDimensions(bytes);
            width.Should().Be(100);
            height.Should().Be(50);
        }

        [Fact]
        public async Task ServeDatasetFile_WithoutWidthQuery_StillReturnsOriginalUnmodified()
        {
            byte[] original = MakeTestPng(400, 200, SKColors.Coral);
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("thumb-local-passthrough", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            PlantImageFile(created.Id, "thumb-local-passthrough", "photo.png", original);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.png");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Should().Equal(original, "no ?w= means the untouched original, not a resize");
        }

        [Fact]
        public async Task ServeDatasetFile_SecondRequestForSameWidth_ReturnsIdenticalCachedBytes()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("thumb-local-cache", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            PlantImageFile(created.Id, "thumb-local-cache", "photo.png", MakeTestPng(300, 300, SKColors.Gold));

            HttpResponseMessage first = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.png?w=80");
            HttpResponseMessage second = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.png?w=80");

            byte[] firstBytes = await first.Content.ReadAsByteArrayAsync();
            byte[] secondBytes = await second.Content.ReadAsByteArrayAsync();
            secondBytes.Should().Equal(firstBytes);
        }

        private async Task<(DatasetDetailDto Dataset, Guid UserId)> CreateStreamingDatasetAsync(bool isPublic = true)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("hf-thumb-test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == created.Id);
            entity.SourceType = DatasetSourceType.HuggingFaceStreaming;
            entity.IsStreaming = true;
            entity.HuggingFaceRepository = "fake/dataset";
            entity.HuggingFaceSplit = "train";
            entity.IsPublic = isPublic;
            Guid userId = entity.CreatedByUserId!.Value;
            await db.SaveChangesAsync();

            return (created, userId);
        }

        private void SetupHfRow(long rowIndex, string imageUrl)
        {
            JsonElement urlElement = JsonSerializer.SerializeToElement(imageUrl);
            HuggingFaceRow row = new()
            {
                RowIndex = rowIndex,
                Columns = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) { ["image_url"] = urlElement },
            };
            HuggingFaceRowsPage page = new()
            {
                Dataset = "fake/dataset",
                Config = null,
                Split = "train",
                NumRowsTotal = 1,
                Rows = new List<HuggingFaceRow> { row },
            };

            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(page);
        }

        [Fact]
        public async Task GetHuggingFaceThumbnail_ForStreamingDataset_ReturnsResizedJpeg()
        {
            (DatasetDetailDto dataset, _) = await CreateStreamingDatasetAsync();
            SetupHfRow(0, "https://fake-hf-asset.example.test/image.png");

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/hf-thumbnail?offset=0&w=120");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            DecodeDimensions(bytes).Width.Should().Be(120);
        }

        [Fact]
        public async Task GetHuggingFaceThumbnail_NonStreamingDataset_ReturnsNotFound()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("not-streaming", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/hf-thumbnail?offset=0&w=120");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetHuggingFaceThumbnail_RowNotReturnedByUpstream_ReturnsNotFound()
        {
            (DatasetDetailDto dataset, _) = await CreateStreamingDatasetAsync();
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((HuggingFaceRowsPage?)null);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/hf-thumbnail?offset=0&w=120");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetHuggingFaceThumbnail_PrivateDatasetNonOwner_ReturnsNotFoundNotForbidden()
        {
            // Ties Phase 3's existence-leak fix to Phase 4's new endpoint: a private streaming
            // dataset must be just as invisible to a non-owner here as it is on every other route.
            (DatasetDetailDto dataset, _) = await CreateStreamingDatasetAsync(isPublic: false);
            SetupHfRow(0, "https://fake-hf-asset.example.test/image.png");

            using HttpClient otherClient = _factory.CreateClient();
            await otherClient.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("hf-thumb-other-user", "hf-thumb-other-user@example.com", "password123"));

            HttpResponseMessage response = await otherClient.GetAsync($"/api/datasets/{dataset.Id}/hf-thumbnail?offset=0&w=120");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetHuggingFaceThumbnail_MissingOffset_ReturnsBadRequest()
        {
            (DatasetDetailDto dataset, _) = await CreateStreamingDatasetAsync();

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/hf-thumbnail?w=120");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>Always returns a real, decodable PNG regardless of the requested URL.</summary>
        private sealed class FakeImageHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                byte[] bytes = MakeTestPng(500, 250, SKColors.SeaGreen);
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return Task.FromResult(response);
            }
        }
    }
}
