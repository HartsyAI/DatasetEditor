using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.Caching;
using DatasetStudio.ClientApp.Services.Interop;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Datasets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.5 review follow-up: the create forms open the viewer while a background import may still
/// be running, so the viewer's first page can be empty. When status polling sees ingestion finish,
/// the cache reloads the first page; before, the viewer stayed empty until a manual reload.
/// </summary>
public sealed class IngestionCompletionReloadTests
{
    private static readonly Guid DatasetId = Guid.NewGuid();

    /// <summary>
    /// Opened while Processing with no items; the next status read says Completed and the items
    /// endpoint now has rows; after the refresh the buffer holds them.
    /// </summary>
    [Fact]
    public async Task StatusRefresh_ReloadsItemsWhenIngestionCompletes()
    {
        FakeApi api = new();
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);

        await cache.LoadFirstPageAsync(DatasetId);
        Assert.Empty(state.Items);

        api.Status = IngestionStatusDto.Completed;
        api.ItemCount = 3;
        await cache.RefreshDatasetStatusAsync();

        Assert.Equal(3, state.Items.Count);
        Assert.Equal(IngestionStatusDto.Completed, cache.CurrentDatasetDetail!.Status);
    }

    /// <summary>
    /// PR 4.4 (audit U-6): a filter change reloads the first page with the filter on the request,
    /// without switching the viewer to its loading state (which unmounted the filter panel the user
    /// was typing in).
    /// </summary>
    [Fact]
    public async Task ApplyFilter_ReloadsWithTheFilter_WithoutTheLoadingState()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 2 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);
        System.Collections.Generic.List<bool> loadingSeen = new();
        state.OnChange += () => loadingSeen.Add(state.IsLoading);

        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "cat", Tags = new[] { "pet" } });

        Assert.Contains(api.Requests, u => u.Contains("/items") && u.Contains("q=cat") && u.Contains("tag=pet"));
        Assert.DoesNotContain(true, loadingSeen);
        Assert.Equal("cat", cache.ActiveFilter!.SearchQuery);
    }

    /// <summary>
    /// PR 4.4 review: a filtered local scan stops after its row budget and may return no matches
    /// with a cursor. The first page and later pages keep following that cursor to the matches;
    /// before, the empty page ended paging and later matches were never shown.
    /// </summary>
    [Fact]
    public async Task SparseFilter_FollowsEmptyScanBudgetPagesToTheMatches()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 5 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);

        api.Pages.Enqueue((0, "r200000"));
        api.Pages.Enqueue((0, "r400000"));
        api.Pages.Enqueue((2, "r600000"));
        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "rare" });

        Assert.Equal(2, state.Items.Count);
        Assert.Contains(api.Requests, u => u.Contains("cursor=r400000"));
        Assert.True(cache.HasMorePages);

        api.Pages.Enqueue((0, "r800000"));
        api.Pages.Enqueue((1, null));
        Assert.True(await cache.LoadNextPageAsync());
        Assert.Equal(3, state.Items.Count);
        Assert.False(cache.HasMorePages);
    }

    /// <summary>A page that hands back its own cursor ends the loop instead of spinning.</summary>
    [Fact]
    public async Task SparseFilter_StopsWhenTheCursorDoesNotAdvance()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 5 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);

        api.Pages.Enqueue((0, "r200000"));
        api.Pages.Enqueue((0, "r200000"));
        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "stuck" });

        Assert.Empty(state.Items);
        Assert.Equal(2, api.Requests.Count(u => u.Contains("q=stuck")));
    }

    /// <summary>
    /// PR 4.4 review: a filter set on one dataset isn't sent with another dataset's requests. It
    /// was, so a local tag filter made an S3 dataset's first page fail with 400.
    /// </summary>
    [Fact]
    public async Task OpeningAnotherDataset_DropsTheFilter()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 2 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);
        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { Tags = new[] { "pet" } });

        Guid other = Guid.NewGuid();
        await cache.LoadFirstPageAsync(other);

        Assert.Null(cache.ActiveFilter);
        string lastItems = api.Requests.Last(u => u.Contains("/items"));
        Assert.Contains(other.ToString(), lastItems);
        Assert.DoesNotContain("tag=", lastItems);
    }

    /// <summary>Reloading the same dataset (after ingestion, say) keeps its filter.</summary>
    [Fact]
    public async Task ReloadingTheSameDataset_KeepsTheFilter()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 2 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);
        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { Tags = new[] { "pet" } });

        await cache.LoadFirstPageAsync(DatasetId);

        Assert.Contains("tag=pet", api.Requests.Last(u => u.Contains("/items")));
    }

    /// <summary>Each load that replaces the items is a new result set; appending a page is not.</summary>
    [Fact]
    public async Task ResultSetVersion_ChangesOnReplaceNotOnAppend()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 5 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        api.Pages.Enqueue((2, "r2"));
        await cache.LoadFirstPageAsync(DatasetId);
        int opened = state.ResultSetVersion;

        api.Pages.Enqueue((2, null));
        await cache.LoadNextPageAsync();
        Assert.Equal(opened, state.ResultSetVersion);

        await cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "x" });
        Assert.NotEqual(opened, state.ResultSetVersion);
    }

    /// <summary>
    /// PR 4.4 review: a filter the server can't apply used to set the dataset-wide error, which
    /// replaced the whole viewer (filter panel included). Now the results and the previous filter
    /// stay, and asking for the same filter again retries it.
    /// </summary>
    [Fact]
    public async Task FilterFailure_KeepsTheResults_AndDoesNotSetTheDatasetError()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 2 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);
        api.Intercept = path => Task.FromResult<HttpStatusCode?>(path.Contains("q=bad") ? HttpStatusCode.BadGateway : null);
        DatasetStudio.DTO.Common.FilterRequest bad = new() { SearchQuery = "bad" };

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.ApplyFilterAsync(bad));

        Assert.Null(state.ErrorMessage);
        Assert.Equal(2, state.Items.Count);
        Assert.Null(cache.ActiveFilter);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.ApplyFilterAsync(bad));
        Assert.Equal(2, api.Requests.Count(u => u.Contains("q=bad")));
    }

    /// <summary>
    /// PR 4.4 review: a filter request that fails after a newer one was made used to restore the
    /// filter from before it, and the newer load (waiting for the page lock) then sent that, so an
    /// unfiltered page showed under the newer search. Now each load carries its own filter.
    /// </summary>
    [Fact]
    public async Task OlderFilterFailure_DoesNotChangeWhatANewerFilterLoads()
    {
        FakeApi api = new() { Status = IngestionStatusDto.Completed, ItemCount = 2 };
        DatasetState state = new();
        DatasetCacheService cache = Create(api, state);
        await cache.LoadFirstPageAsync(DatasetId);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Intercept = async path =>
        {
            if (!path.Contains("q=first"))
            {
                return null;
            }
            firstArrived.TrySetResult();
            await release.Task;
            return HttpStatusCode.InternalServerError;
        };

        Task first = cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "first" });
        await firstArrived.Task;
        Task second = cache.ApplyFilterAsync(new DatasetStudio.DTO.Common.FilterRequest { SearchQuery = "second" });
        release.SetResult();

        await Assert.ThrowsAsync<HttpRequestException>(() => first);
        await second;

        Assert.Equal("second", cache.ActiveFilter!.SearchQuery);
        string lastItems = api.Requests.Last(u => u.Contains("/items"));
        Assert.Contains("q=second", lastItems);
        Assert.Null(state.ErrorMessage);
    }

    /// <summary>Only the Pending/Processing → Completed transition reloads; other refreshes don't.</summary>
    [Theory]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Completed, true)]
    [InlineData(IngestionStatusDto.Pending, IngestionStatusDto.Completed, true)]
    [InlineData(IngestionStatusDto.Completed, IngestionStatusDto.Completed, false)]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Processing, false)]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Failed, false)]
    public void FinishedIngesting_OnlyForTheCompletionTransition(IngestionStatusDto before, IngestionStatusDto after, bool expected)
    {
        DatasetDto previous = new() { Id = DatasetId, Name = "x", Status = before };
        DatasetDto current = previous with { Status = after };

        Assert.Equal(expected, DatasetCacheService.FinishedIngesting(previous, current));
    }

    private static DatasetCacheService Create(FakeApi api, DatasetState state)
    {
        DatasetApiClient client = new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5000/") });
        IndexedDbCache indexedDb = new(new IndexedDbInterop(new NoJsRuntime()), NullLogger<IndexedDbCache>.Instance);
        return new DatasetCacheService(client, state, indexedDb, new ApiKeyState(), NullLogger<DatasetCacheService>.Instance);
    }

    /// <summary>Serves the dataset record and its items page from mutable fields.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        public IngestionStatusDto Status { get; set; } = IngestionStatusDto.Processing;
        public int ItemCount { get; set; }

        public System.Collections.Generic.List<string> Requests { get; } = new();

        /// <summary>When not empty, each items request takes the next (count, next cursor) from here.</summary>
        public System.Collections.Generic.Queue<(int Count, string? Next)> Pages { get; } = new();

        /// <summary>When set, runs first for every request; a status it returns is sent as the response.</summary>
        public Func<string, Task<HttpStatusCode?>>? Intercept { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Intercept is not null && await Intercept(request.RequestUri!.PathAndQuery) is HttpStatusCode status)
            {
                Requests.Add(request.RequestUri!.PathAndQuery);
                return new HttpResponseMessage(status);
            }
            return await Respond(request);
        }

        private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            string path = request.RequestUri!.AbsolutePath;
            bool itemsRequest = path.EndsWith("/items", StringComparison.Ordinal);
            (int count, string? next) = itemsRequest && Pages.Count > 0 ? Pages.Dequeue() : (ItemCount, null);
            object body = itemsRequest
                ? new
                {
                    items = Enumerable(count),
                    nextCursor = next,
                    totalCount = (long?)ItemCount
                }
                : new { id = DatasetId, name = "import", status = (int)Status, totalItems = ItemCount };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }

        private static object[] Enumerable(int count)
        {
            object[] items = new object[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new { id = Guid.NewGuid(), datasetId = DatasetId, title = $"item {i}", imageUrl = $"/i/{i}.jpg" };
            }
            return items;
        }
    }

    /// <summary>The IndexedDB cache is off in these tests; any JS call would be a test bug.</summary>
    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected JS call {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected JS call {identifier}");
    }
}
