using System;
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

    /// <summary>Only the Pending/Processing → Completed transition reloads; other refreshes don't.</summary>
    [Theory]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Completed, true)]
    [InlineData(IngestionStatusDto.Pending, IngestionStatusDto.Completed, true)]
    [InlineData(IngestionStatusDto.Completed, IngestionStatusDto.Completed, false)]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Processing, false)]
    [InlineData(IngestionStatusDto.Processing, IngestionStatusDto.Failed, false)]
    public void FinishedIngesting_OnlyForTheCompletionTransition(IngestionStatusDto before, IngestionStatusDto after, bool expected)
    {
        DatasetDetailDto previous = new() { Id = DatasetId, Name = "x", Status = before };
        DatasetDetailDto current = previous with { Status = after };

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            object body = path.EndsWith("/items", StringComparison.Ordinal)
                ? new
                {
                    items = Enumerable(ItemCount),
                    nextCursor = (string?)null,
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
