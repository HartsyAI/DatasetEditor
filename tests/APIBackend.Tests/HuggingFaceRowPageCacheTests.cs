using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Integration;
using FluentAssertions;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Phase 4: the hf-thumbnail endpoint re-fetches rows through this cache so that scrolling
    /// a grid full of thumbnails collapses ~100 per-card lookups into ~1 upstream call per page
    /// window. These tests cover the two behaviors that matter: real caching (a hit doesn't
    /// re-fetch), and NOT caching a transient upstream failure for the full TTL -- a bug found
    /// live during Phase 4's real-run verification (a genuine transient connection reset on one
    /// GetRowsAsync call would otherwise have 404'd every thumbnail in that page's 100-row
    /// window for the next 5 minutes).
    /// </summary>
    public sealed class HuggingFaceRowPageCacheTests
    {
        private static HuggingFaceRowsPage MakePage(long pageStart, int count)
        {
            List<HuggingFaceRow> rows = new();
            for (int i = 0; i < count; i++)
            {
                rows.Add(new HuggingFaceRow
                {
                    RowIndex = pageStart + i,
                    Columns = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["image_url"] = JsonSerializer.SerializeToElement($"https://example.test/{pageStart + i}.png"),
                    },
                });
            }
            return new HuggingFaceRowsPage { Dataset = "fake/dataset", Split = "train", NumRowsTotal = 1000, Rows = rows };
        }

        [Fact]
        public async Task GetRowAsync_TwoOffsetsInSamePageWindow_OnlyFetchesUpstreamOnce()
        {
            Mock<IHuggingFaceDatasetServerClient> client = new();
            int callCount = 0;
            client.Setup(c => c.GetRowsAsync(
                    "fake/dataset", null, "train", 0, 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { callCount++; return MakePage(0, 100); });

            HuggingFaceRowPageCache cache = new();

            HuggingFaceRow? rowA = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 3, null, CancellationToken.None);
            HuggingFaceRow? rowB = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 47, null, CancellationToken.None);

            rowA!.RowIndex.Should().Be(3);
            rowB!.RowIndex.Should().Be(47);
            callCount.Should().Be(1, "both offsets fall in the same 0-99 page window");
        }

        [Fact]
        public async Task GetRowAsync_DifferentPageWindows_FetchesUpstreamOncePerWindow()
        {
            Mock<IHuggingFaceDatasetServerClient> client = new();
            client.Setup(c => c.GetRowsAsync("fake/dataset", null, "train", 0, 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakePage(0, 100));
            client.Setup(c => c.GetRowsAsync("fake/dataset", null, "train", 100, 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakePage(100, 100));

            HuggingFaceRowPageCache cache = new();

            HuggingFaceRow? rowA = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 10, null, CancellationToken.None);
            HuggingFaceRow? rowB = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 150, null, CancellationToken.None);

            rowA!.RowIndex.Should().Be(10);
            rowB!.RowIndex.Should().Be(150);
            client.Verify(c => c.GetRowsAsync("fake/dataset", null, "train", 0, 100, null, It.IsAny<CancellationToken>()), Times.Once);
            client.Verify(c => c.GetRowsAsync("fake/dataset", null, "train", 100, 100, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetRowAsync_UpstreamReturnsNull_DoesNotPoisonSubsequentCallsForTheFullTtl()
        {
            Mock<IHuggingFaceDatasetServerClient> client = new();
            int callCount = 0;
            client.Setup(c => c.GetRowsAsync(
                    "fake/dataset", null, "train", 0, 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    // First call simulates the transient failure GetRowsAsync itself already
                    // swallows (its own try/catch returns null); second call succeeds.
                    return callCount == 1 ? null : MakePage(0, 100);
                });

            HuggingFaceRowPageCache cache = new();

            HuggingFaceRow? first = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 5, null, CancellationToken.None);
            HuggingFaceRow? second = await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 5, null, CancellationToken.None);

            first.Should().BeNull();
            second.Should().NotBeNull("a failed fetch must not be cached for the full TTL -- the next request should retry immediately");
            second!.RowIndex.Should().Be(5);
            callCount.Should().Be(2, "the retry must actually hit the upstream client again, not reuse the cached failure");
        }
        /// <summary>Audit H-7: the cache holds at most its page limit, evicting the least recently used.</summary>
        [Fact]
        public async Task GetRowAsync_ManyPages_KeepsAtMostTheLimit()
        {
            Mock<IHuggingFaceDatasetServerClient> client = new();
            client.Setup(c => c.GetRowsAsync("fake/dataset", null, "train", It.IsAny<int>(), 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? _, string _, int offset, int _, string? _, CancellationToken _) => MakePage(offset, 100));
            HuggingFaceRowPageCache cache = new(maxPages: 3);

            for (int page = 0; page < 10; page++)
            {
                await cache.GetRowAsync(client.Object, "fake/dataset", null, "train", page * 100, null, CancellationToken.None);
            }

            cache.Count.Should().Be(3);
        }

        /// <summary>
        /// Audit H-7: one caller cancelling doesn't fail another caller waiting on the same page.
        /// The first caller's token used to be the shared fetch's token.
        /// </summary>
        [Fact]
        public async Task GetRowAsync_FirstCallerCancels_SecondCallerStillGetsTheRow()
        {
            TaskCompletionSource<HuggingFaceRowsPage?> upstream = new();
            Mock<IHuggingFaceDatasetServerClient> client = new();
            client.Setup(c => c.GetRowsAsync("fake/dataset", null, "train", 0, 100, null, It.IsAny<CancellationToken>()))
                .Returns((string _, string? _, string _, int _, int _, string? _, CancellationToken token) => upstream.Task.WaitAsync(token));
            HuggingFaceRowPageCache cache = new();
            using CancellationTokenSource firstCaller = new();

            Task<HuggingFaceRow?> first = cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 1, null, firstCaller.Token);
            Task<HuggingFaceRow?> second = cache.GetRowAsync(client.Object, "fake/dataset", null, "train", 2, null, CancellationToken.None);
            firstCaller.Cancel();
            upstream.SetResult(MakePage(0, 100));

            await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
            (await second)!.RowIndex.Should().Be(2);
        }
    }
}
