using System;
using System.IO;
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
    }
}
