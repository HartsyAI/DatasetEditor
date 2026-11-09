using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Unit tests for ThumbnailService: resize correctness, disk-cache hit/miss behavior, and
    /// the SourceNotFound/Undecodable fallback outcomes ServeDatasetFile and the hf-thumbnail
    /// endpoint both depend on. Real SkiaSharp-encoded bytes throughout -- a hand-rolled byte
    /// array with just JPEG magic numbers (as used elsewhere in this test suite for endpoints
    /// that only round-trip bytes) won't actually decode, which is exactly what these tests
    /// need to exercise.
    /// </summary>
    public sealed class ThumbnailServiceTests : IDisposable
    {
        private readonly string _cacheRoot = Path.Combine(
            Path.GetTempPath(), "DatasetStudioTests", "thumbnail-service-unit", Guid.NewGuid().ToString("N"));

        private ThumbnailService CreateService() => new(_cacheRoot, NullLogger<ThumbnailService>.Instance);

        public void Dispose()
        {
            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, recursive: true);
            }
        }

        /// <summary>Encodes a solid-color PNG of the given size -- real, SkiaSharp-decodable bytes.</summary>
        private static byte[] MakeTestImage(int width, int height, SKColor color)
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

        [Fact]
        public async Task GetOrCreateJpegAsync_ResizesToRequestedWidth_PreservingAspectRatio()
        {
            ThumbnailService service = CreateService();
            byte[] original = MakeTestImage(200, 100, SKColors.CornflowerBlue);

            ThumbnailResult result = await service.GetOrCreateJpegAsync(
                "cache-key-a", 100, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.Generated);
            (int width, int height) = DecodeDimensions(result.Bytes!);
            width.Should().Be(100);
            height.Should().Be(50); // 200x100 at half width -> half height, aspect preserved
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_NeverUpscales_NarrowerThanRequestedWidthServedAtNativeSize()
        {
            ThumbnailService service = CreateService();
            byte[] original = MakeTestImage(50, 50, SKColors.Coral);

            ThumbnailResult result = await service.GetOrCreateJpegAsync(
                "cache-key-b", 400, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.Generated);
            (int width, int height) = DecodeDimensions(result.Bytes!);
            width.Should().Be(50);
            height.Should().Be(50);
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_SecondCallForSameKeyAndWidth_ServesFromCacheWithoutReloadingOriginal()
        {
            ThumbnailService service = CreateService();
            byte[] original = MakeTestImage(120, 80, SKColors.SeaGreen);
            int loadCount = 0;

            Func<CancellationToken, Task<byte[]?>> load = _ =>
            {
                loadCount++;
                return Task.FromResult<byte[]?>(original);
            };

            ThumbnailResult first = await service.GetOrCreateJpegAsync("cache-key-c", 60, load, CancellationToken.None);
            ThumbnailResult second = await service.GetOrCreateJpegAsync("cache-key-c", 60, load, CancellationToken.None);

            first.Outcome.Should().Be(ThumbnailOutcome.Generated);
            second.Outcome.Should().Be(ThumbnailOutcome.Generated);
            second.Bytes.Should().Equal(first.Bytes);
            loadCount.Should().Be(1, "the second request should be served from the on-disk cache, not regenerated");
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_DifferentWidthSameKey_GeneratesASeparateCacheEntry()
        {
            ThumbnailService service = CreateService();
            byte[] original = MakeTestImage(300, 150, SKColors.Gold);

            ThumbnailResult narrow = await service.GetOrCreateJpegAsync(
                "cache-key-d", 100, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);
            ThumbnailResult wide = await service.GetOrCreateJpegAsync(
                "cache-key-d", 200, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            DecodeDimensions(narrow.Bytes!).Width.Should().Be(100);
            DecodeDimensions(wide.Bytes!).Width.Should().Be(200);
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_SourceNotFound_ReturnsSourceNotFoundOutcomeWithNoBytes()
        {
            ThumbnailService service = CreateService();

            ThumbnailResult result = await service.GetOrCreateJpegAsync(
                "cache-key-e", 100, _ => Task.FromResult<byte[]?>(null), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.SourceNotFound);
            result.Bytes.Should().BeNull();
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_UndecodableBytes_ReturnsOriginalBytesForPassthroughFallback()
        {
            ThumbnailService service = CreateService();
            byte[] junk = { 0x01, 0x02, 0x03, 0x04, 0x05 }; // not a real image, deliberately

            ThumbnailResult result = await service.GetOrCreateJpegAsync(
                "cache-key-f", 100, _ => Task.FromResult<byte[]?>(junk), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.Undecodable);
            result.Bytes.Should().Equal(junk, "the caller falls back to serving the original bytes as-is");
        }

        [Fact]
        public async Task GetOrCreateJpegAsync_RequestedWidthClamped_NeverExceedsMaxWidthConstant()
        {
            ThumbnailService service = CreateService();
            byte[] original = MakeTestImage(2000, 1000, SKColors.Plum);

            ThumbnailResult result = await service.GetOrCreateJpegAsync(
                "cache-key-g", 5000, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.Generated);
            DecodeDimensions(result.Bytes!).Width.Should().BeLessThanOrEqualTo(1024);
        }
        private ThumbnailService CreateService(ThumbnailOptions options) => new(_cacheRoot, NullLogger<ThumbnailService>.Instance, options);

        /// <summary>Audit T-1: the cache is cut back to its size cap, oldest thumbnails first.</summary>
        [Fact]
        public async Task SweepAsync_TrimsTheCacheToItsCap_OldestFirst()
        {
            ThumbnailService service = CreateService(new ThumbnailOptions { MaxCacheBytes = 4_000, SweepEveryWrites = int.MaxValue });
            byte[] original = MakeTestImage(300, 300, SKColors.SeaGreen);
            for (int i = 0; i < 12; i++)
            {
                await service.GetOrCreateJpegAsync($"sweep-{i}", 200, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);
            }
            List<FileInfo> before = new DirectoryInfo(_cacheRoot).EnumerateFiles("*.jpg", SearchOption.AllDirectories).ToList();
            before.Sum(f => f.Length).Should().BeGreaterThan(4_000, "the test needs a cache over its cap");
            FileInfo newest = before.OrderBy(f => f.LastWriteTimeUtc).Last();

            await service.SweepAsync();

            List<FileInfo> after = new DirectoryInfo(_cacheRoot).EnumerateFiles("*.jpg", SearchOption.AllDirectories).ToList();
            after.Sum(f => f.Length).Should().BeLessThanOrEqualTo(4_000);
            after.Select(f => f.FullName).Should().Contain(newest.FullName, "the most recent thumbnails are kept");
        }

        /// <summary>Audit S-2, T-3: an original over the byte limit isn't thumbnailed, and the loader's bounded read stops early.</summary>
        [Fact]
        public async Task GetOrCreateJpegAsync_SourceOverByteLimit_IsTooLarge()
        {
            ThumbnailService service = CreateService(new ThumbnailOptions { MaxSourceBytes = 500 });
            byte[] original = MakeTestImage(300, 300, SKColors.Navy);
            original.Length.Should().BeGreaterThan(500);

            ThumbnailResult direct = await service.GetOrCreateJpegAsync("big-a", 100, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);
            ThumbnailResult streamed = await service.GetOrCreateJpegAsync("big-b", 100,
                ct => ThumbnailSource.ReadLimitedAsync(new MemoryStream(original), service.MaxSourceBytes, ct)!, CancellationToken.None);

            direct.Outcome.Should().Be(ThumbnailOutcome.TooLarge);
            streamed.Outcome.Should().Be(ThumbnailOutcome.TooLarge);
        }

        /// <summary>Audit T-3: an image whose header claims too many pixels is refused before decoding.</summary>
        [Fact]
        public async Task GetOrCreateJpegAsync_TooManyPixels_IsRefusedBeforeDecoding()
        {
            ThumbnailService service = CreateService(new ThumbnailOptions { MaxSourcePixels = 1_000 });
            byte[] original = MakeTestImage(50, 50, SKColors.Orange);

            ThumbnailResult result = await service.GetOrCreateJpegAsync("bomb", 32, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.TooLarge);
            result.Bytes.Should().BeNull();
        }

        /// <summary>A large JPEG is decoded at reduced scale and still comes out at exactly the requested size.</summary>
        [Fact]
        public async Task GetOrCreateJpegAsync_LargeJpeg_ScaledDecodeKeepsExactSize()
        {
            ThumbnailService service = CreateService();
            using SKSurface surface = SKSurface.Create(new SKImageInfo(4000, 3000));
            surface.Canvas.Clear(SKColors.Teal);
            using SKImage image = surface.Snapshot();
            using SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            byte[] original = jpeg.ToArray();

            ThumbnailResult result = await service.GetOrCreateJpegAsync("scaled", 256, _ => Task.FromResult<byte[]?>(original), CancellationToken.None);

            result.Outcome.Should().Be(ThumbnailOutcome.Generated);
            result.Width.Should().Be(256);
            DecodeDimensions(result.Bytes!).Should().Be((256, 192));
        }
    }
}
