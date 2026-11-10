using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.Storage;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// End-to-end coverage for Phase 5's S3 pipeline against a real S3-compatible container
    /// (Adobe S3Mock, see <see cref="S3ContainerFixture"/>): connect a dataset to a bucket, list
    /// items live (no bulk ingestion), serve/thumbnail objects through the proxy endpoint, and
    /// confirm the bucket allow-list + S3KeySafety guards actually block what they're supposed
    /// to. Uses the real AWS S3 SDK against a live server, not a mock -- this is exactly the layer
    /// a mock would hide the AWS SDK v4 checksum incompatibility found while building this (see
    /// S3StorageService's RequestChecksumCalculation comment).
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class S3EndpointsTests : IClassFixture<S3ContainerFixture>, IAsyncLifetime
    {
        private const string ProfileName = "test-s3";
        private const string Bucket = "test-bucket";

        private readonly PostgresContainerFixture _postgres;
        private readonly S3ContainerFixture _s3;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private AmazonS3Client _s3SeedClient = default!;
        private readonly CapturingLoggerProvider _logs = new();

        public S3EndpointsTests(PostgresContainerFixture postgres, S3ContainerFixture s3)
        {
            _postgres = postgres;
            _s3 = s3;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);

            // Must be set before the host is first built (CreateClient()/Services below) --
            // same timing constraint Phase 4's ThumbnailEndpointsTests documented.
            _factory.ConfigureTestServices = services =>
            {
                S3StorageProfileConfig profile = new()
                {
                    Name = ProfileName,
                    ServiceUrl = _s3.ServiceUrl,
                    AccessKey = _s3.AccessKey,
                    SecretKey = _s3.SecretKey,
                    ForcePathStyle = true,
                    AllowedBuckets = new() { Bucket },
                };
                services.AddSingleton<IS3StorageProfileResolver>(new S3StorageProfileResolver(new[] { profile }));
                services.AddSingleton<ILoggerProvider>(_logs);
            };

            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();

            await _client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("s3-test-user", "s3-test-user@example.com", "password123"));

            AmazonS3Config s3Config = new()
            {
                ServiceURL = _s3.ServiceUrl,
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            _s3SeedClient = new AmazonS3Client(new BasicAWSCredentials(_s3.AccessKey, _s3.SecretKey), s3Config);
            try
            {
                // The S3 container (and this bucket) is shared across every test in this
                // collection -- xUnit creates a fresh S3EndpointsTests instance per [Fact], so
                // this runs once per test, not once total.
                await _s3SeedClient.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
            }
            catch (BucketAlreadyOwnedByYouException)
            {
                // Expected from the second test onward.
            }
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            _s3SeedClient.Dispose();
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

        private async Task PutObjectAsync(string key, byte[] bytes)
        {
            await _s3SeedClient.PutObjectAsync(new PutObjectRequest
            {
                BucketName = Bucket,
                Key = key,
                InputStream = new MemoryStream(bytes),
            });
        }

        private async Task<DatasetDetailDto> CreateAndConnectDatasetAsync(string? prefix = "images/", bool isPublic = true)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest($"s3-test-{Guid.NewGuid():N}", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage importResponse = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-s3", new ImportS3DatasetRequest(ProfileName, Bucket, prefix));
            string importBody = await importResponse.Content.ReadAsStringAsync();
            importResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, importBody);

            if (!isPublic)
            {
                using IServiceScope scope = _factory.Services.CreateScope();
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == created.Id);
                entity.IsPublic = false;
                await db.SaveChangesAsync();
            }

            return (await (await _client.GetAsync($"/api/datasets/{created.Id}")).Content.ReadFromJsonAsync<DatasetDetailDto>())!;
        }

        [Fact]
        public async Task ImportFromS3_ValidProfileAndBucket_ConnectsAndPersistsMetadata()
        {
            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: "images/");

            dataset.SourceType.Should().Be(DatasetSourceType.ExternalS3Streaming);
            dataset.IsStreaming.Should().BeTrue();
            dataset.Status.Should().Be(IngestionStatusDto.Completed);
            dataset.S3StorageProfileName.Should().Be(ProfileName);
            dataset.S3Bucket.Should().Be(Bucket);
            dataset.S3Prefix.Should().Be("images/");
        }

        [Fact]
        public async Task ImportFromS3_UnknownProfile_ReturnsBadRequest()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("s3-bad-profile", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage response = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-s3", new ImportS3DatasetRequest("no-such-profile", Bucket, null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task ImportFromS3_BucketNotInAllowList_ReturnsBadRequest()
        {
            // The core security property: naming a bucket in the request is not sufficient --
            // it must also be on the profile's own AllowedBuckets list.
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("s3-bad-bucket", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage response = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-s3", new ImportS3DatasetRequest(ProfileName, "some-other-bucket", null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetDatasetItems_S3Dataset_ListsBucketObjectsSkippingNonImageKeys()
        {
            string prefix = $"listing-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}photo-1.png", MakeTestPng(10, 10, SKColors.Red));
            await PutObjectAsync($"{prefix}photo-2.png", MakeTestPng(10, 10, SKColors.Blue));
            await PutObjectAsync($"{prefix}manifest.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=100");
            PageResponse<DatasetItemDto>? page = await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>();

            page.Should().NotBeNull();
            page!.Items.Should().HaveCount(2, "manifest.json is not an image and must be skipped");
            page.Items.Should().OnlyContain(i => i.ImageUrl != null && i.ImageUrl.Contains("/s3-file?key="));
        }

        /// <summary>
        /// Audit S-3: a prefix stored without a trailing slash used to be listed as a raw string
        /// prefix, so <c>sib-x</c> also listed <c>sib-xEXTRA/…</c>, keys GetS3File then refused.
        /// The listing treats the prefix as a folder, like the key check does.
        /// </summary>
        [Fact]
        public async Task GetDatasetItems_S3PrefixWithoutTrailingSlash_DoesNotListSiblingPrefixes()
        {
            string prefix = $"sib-{Guid.NewGuid():N}";
            await PutObjectAsync($"{prefix}/mine.png", MakeTestPng(5, 5, SKColors.Red));
            await PutObjectAsync($"{prefix}EXTRA/theirs.png", MakeTestPng(5, 5, SKColors.Blue));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=100");
            PageResponse<DatasetItemDto>? page = await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>();

            page!.Items.Select(i => i.ExternalId).Should().Equal($"{prefix}/mine.png");
        }

        /// <summary>Audit S-3: an object whose name merely contains ".." is served, not refused.</summary>
        [Fact]
        public async Task GetS3File_KeyWithDoubleDotInName_IsServed()
        {
            string prefix = $"dots-{Guid.NewGuid():N}/";
            byte[] original = MakeTestPng(8, 8, SKColors.Purple);
            await PutObjectAsync($"{prefix}photo..final.png", original);

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString($"{prefix}photo..final.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(original);
        }

        [Fact]
        public async Task GetDatasetItems_S3Dataset_PaginatesViaContinuationTokenNotNumericOffset()
        {
            string prefix = $"paging-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}a.png", MakeTestPng(5, 5, SKColors.Red));
            await PutObjectAsync($"{prefix}b.png", MakeTestPng(5, 5, SKColors.Green));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage firstResponse = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=1");
            PageResponse<DatasetItemDto>? firstPage = await firstResponse.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>();
            firstPage!.Items.Should().HaveCount(1);
            firstPage.NextCursor.Should().NotBeNullOrEmpty();

            HttpResponseMessage secondResponse = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/items?pageSize=1&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
            PageResponse<DatasetItemDto>? secondPage = await secondResponse.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>();

            secondPage!.Items.Should().HaveCount(1);
            secondPage.Items[0].ExternalId.Should().NotBe(firstPage.Items[0].ExternalId, "the second page must return a different object, not repeat the first");
        }

        [Fact]
        public async Task GetS3File_ServesOriginalBytesUnmodifiedWithoutWidthQuery()
        {
            string prefix = $"serve-{Guid.NewGuid():N}/";
            byte[] original = MakeTestPng(200, 100, SKColors.CornflowerBlue);
            await PutObjectAsync($"{prefix}photo.png", original);

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString($"{prefix}photo.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            byte[] served = await response.Content.ReadAsByteArrayAsync();
            served.Should().Equal(original);
        }

        [Fact]
        public async Task GetS3File_WithWidthQuery_ReturnsResizedJpeg()
        {
            string prefix = $"thumb-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}photo.png", MakeTestPng(400, 200, SKColors.Gold));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString($"{prefix}photo.png")}&w=100");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            using SKBitmap decoded = SKBitmap.Decode(bytes);
            decoded.Width.Should().Be(100);
        }

        [Fact]
        public async Task GetS3File_KeyOutsideDatasetPrefix_ReturnsNotFound()
        {
            // The S3 equivalent of ServeDatasetFileSecurityTests' symlink-escape guard: a
            // dataset scoped to one prefix must not be usable to read a key outside it, even
            // though both keys live in the same bucket the profile can technically reach.
            string prefix = $"scoped-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}photo.png", MakeTestPng(10, 10, SKColors.Red));
            await PutObjectAsync("outside-prefix/secret.png", MakeTestPng(10, 10, SKColors.Black));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString("outside-prefix/secret.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            // Audit F-4: the refusal is a security event and must be logged, not just 404'd.
            _logs.WarningsContaining(dataset.Id.ToString(), "outside-prefix/secret.png").Should().NotBeEmpty();
        }

        /// <summary>
        /// Audit F-1, applied to S3 as well: a non-image object inside the dataset's own prefix (a
        /// manifest, a credentials file) is not served, even though the key check passes.
        /// </summary>
        [Fact]
        public async Task GetS3File_NonImageKeyInsidePrefix_ReturnsNotFound()
        {
            string prefix = $"nonimage-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}.env", System.Text.Encoding.UTF8.GetBytes("SECRET=1"));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString($"{prefix}.env")}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            _logs.WarningsContaining(dataset.Id.ToString(), $"{prefix}.env").Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetS3File_PrivateDatasetNonOwner_ReturnsNotFoundNotForbidden()
        {
            string prefix = $"private-{Guid.NewGuid():N}/";
            await PutObjectAsync($"{prefix}photo.png", MakeTestPng(10, 10, SKColors.Red));

            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: prefix, isPublic: false);

            using HttpClient otherClient = _factory.CreateClient();
            await otherClient.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("s3-other-user", "s3-other-user@example.com", "password123"));

            HttpResponseMessage response = await otherClient.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString($"{prefix}photo.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetS3File_NonExistentKey_ReturnsNotFound()
        {
            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: "images/");

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/s3-file?key={Uri.EscapeDataString("images/does-not-exist.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        /// <summary>
        /// Audit S-4: a failed listing used to be swallowed into an empty 200 page with no log, so a
        /// broken bucket looked like an empty dataset. It is logged and reported as 502.
        /// </summary>
        [Fact]
        public async Task GetDatasetItems_S3ListingFails_Returns502AndLogs()
        {
            DatasetDetailDto dataset = await CreateAndConnectDatasetAsync(prefix: "images/");
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == dataset.Id);
                entity.S3Bucket = $"no-such-bucket-{Guid.NewGuid():N}"[..30];
                await db.SaveChangesAsync();
            }

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");

            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            _logs.WarningsContaining(dataset.Id.ToString()).Should().NotBeEmpty();
        }
    }
}
