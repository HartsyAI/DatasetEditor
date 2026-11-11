using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DatasetStudio.APIBackend.Services.Storage;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit S-1: a public S3 dataset made the server an anonymous, unmetered egress proxy for its
    /// prefix, paid for with the server's credentials. Originals larger than
    /// <c>Storage:S3MaxServeBytes</c> are no longer proxied, and the route has a per-caller rate
    /// limit (<c>RateLimiting:S3FilePermitsPerMinute</c>). Separate from S3EndpointsTests because
    /// both limits are set deliberately low here.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class S3EgressLimitsTests : IClassFixture<S3ContainerFixture>, IAsyncLifetime
    {
        private const string ProfileName = "egress-s3";
        private const string Bucket = "egress-bucket";
        private const int MaxServeBytes = 2_000;
        private const int PermitsPerMinute = 3;

        private readonly PostgresContainerFixture _postgres;
        private readonly S3ContainerFixture _s3;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private AmazonS3Client _seed = default!;

        public S3EgressLimitsTests(PostgresContainerFixture postgres, S3ContainerFixture s3)
        {
            _postgres = postgres;
            _s3 = s3;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigOverrides["Storage:S3MaxServeBytes"] = MaxServeBytes.ToString();
            _factory.ConfigOverrides["RateLimiting:S3FilePermitsPerMinute"] = PermitsPerMinute.ToString();
            _factory.ConfigureTestServices = services =>
            {
                S3StorageProfileConfig profile = new()
                {
                    Name = ProfileName,
                    ServiceUrl = _s3.ServiceUrl,
                    AccessKey = _s3.AccessKey,
                    SecretKey = _s3.SecretKey,
                    AllowedBuckets = new() { Bucket },
                };
                services.AddSingleton<IS3StorageProfileResolver>(new S3StorageProfileResolver(new[] { profile }));
            };
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
            await _client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("s3-egress-user", "s3-egress-user@example.com", "password123"));

            _seed = new AmazonS3Client(new BasicAWSCredentials(_s3.AccessKey, _s3.SecretKey), new AmazonS3Config
            {
                ServiceURL = _s3.ServiceUrl,
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
            try
            {
                await _seed.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
            }
            catch (BucketAlreadyOwnedByYouException)
            {
            }
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            _seed.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        /// <summary>A PNG of random pixels, so it doesn't compress below the size cap.</summary>
        private static byte[] MakeNoisyPng(int width, int height)
        {
            Random random = new(42);
            using SKBitmap bitmap = new(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                }
            }
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private async Task<Guid> ConnectDatasetAsync(string prefix)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest($"egress-{Guid.NewGuid():N}", null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;
            HttpResponseMessage import = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-s3", new ImportS3DatasetRequest(ProfileName, Bucket, prefix));
            import.StatusCode.Should().Be(HttpStatusCode.Accepted);
            return created.Id;
        }

        private async Task PutAsync(string key, byte[] bytes)
        {
            await _seed.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = key, InputStream = new MemoryStream(bytes) });
        }

        [Fact]
        public async Task GetS3File_OriginalOverTheServeCap_IsNotProxied_ButItsThumbnailIs()
        {
            string prefix = $"big-{Guid.NewGuid():N}/";
            byte[] large = MakeNoisyPng(64, 64);
            large.Length.Should().BeGreaterThan(MaxServeBytes, "the test object must exceed the cap");
            await PutAsync($"{prefix}large.png", large);
            Guid datasetId = await ConnectDatasetAsync(prefix);
            string key = Uri.EscapeDataString($"{prefix}large.png");

            HttpResponseMessage original = await _client.GetAsync($"/api/datasets/{datasetId}/s3-file?key={key}");
            HttpResponseMessage thumbnail = await _client.GetAsync($"/api/datasets/{datasetId}/s3-file?key={key}&w=32");

            original.StatusCode.Should().Be(HttpStatusCode.NotFound);
            thumbnail.StatusCode.Should().Be(HttpStatusCode.OK);
            thumbnail.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        }

        [Fact]
        public async Task GetS3File_OriginalUnderTheServeCap_IsServed()
        {
            string prefix = $"small-{Guid.NewGuid():N}/";
            byte[] small = MakeNoisyPng(4, 4);
            small.Length.Should().BeLessThan(MaxServeBytes);
            await PutAsync($"{prefix}small.png", small);
            Guid datasetId = await ConnectDatasetAsync(prefix);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{datasetId}/s3-file?key={Uri.EscapeDataString($"{prefix}small.png")}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(small);
        }

        [Fact]
        public async Task GetS3File_PastTheRateLimit_Returns429()
        {
            string prefix = $"rate-{Guid.NewGuid():N}/";
            await PutAsync($"{prefix}a.png", MakeNoisyPng(4, 4));
            Guid datasetId = await ConnectDatasetAsync(prefix);
            string url = $"/api/datasets/{datasetId}/s3-file?key={Uri.EscapeDataString($"{prefix}a.png")}";

            for (int i = 0; i < PermitsPerMinute; i++)
            {
                (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
            }
            HttpResponseMessage limited = await _client.GetAsync(url);

            limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
    }
}
