using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.DomainModels.Datasets;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using Xunit;

namespace DatasetStudio.Tests.ClientApp
{
    /// <summary>
    /// The client side of audit E-4 (edits send If-Match with the item's version, take the new
    /// version from the reply, and reload the item on 412) and E-1 (a bulk edit says how many items
    /// couldn't be edited and why, instead of only showing a smaller count).
    /// </summary>
    public sealed class ItemEditVersionTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public List<HttpRequestMessage> Requests { get; } = new();

            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
                return _respond(request);
            }
        }

        private static (ItemEditService Service, DatasetState State, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond, params DatasetItemDto[] items)
        {
            StubHandler handler = new(respond);
            DatasetApiClient api = new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") });
            DatasetState state = new();
            state.LoadDataset(new Dataset { Id = "ds", Name = "test" }, items.ToList());
            return (new ItemEditService(api, state), state, handler);
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body) };

        [Fact]
        public async Task UpdateItem_SendsIfMatchWithTheItemsVersion_AndKeepsTheNewVersion()
        {
            DatasetItemDto item = new() { Id = Guid.NewGuid(), DatasetId = Guid.NewGuid(), Title = "before", Version = "41" };
            (ItemEditService service, DatasetState state, StubHandler handler) = Build(
                _ => Json(HttpStatusCode.OK, new ItemEditResponse { ItemId = item.Id, DatasetId = item.DatasetId, Version = "42", Title = "after" }),
                item);

            ItemUpdateOutcome outcome = await service.UpdateItemAsync(item, title: "after");

            Assert.Equal(ItemUpdateOutcome.Saved, outcome);
            Assert.Equal("\"41\"", string.Join(",", handler.Requests[0].Headers.GetValues("If-Match")));
            DatasetItemDto stored = state.Items.Single();
            Assert.Equal("after", stored.Title);
            Assert.Equal("42", stored.Version);
        }

        [Fact]
        public async Task UpdateItem_WithoutAVersion_SendsNoIfMatch()
        {
            DatasetItemDto item = new() { Id = Guid.NewGuid(), Title = "cached before versions existed" };
            (ItemEditService service, _, StubHandler handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK), item);

            await service.UpdateItemAsync(item, isFavorite: true);

            Assert.False(handler.Requests[0].Headers.Contains("If-Match"));
        }

        [Fact]
        public async Task UpdateItem_On412_ReportsAConflictAndReloadsTheItemFromTheReply()
        {
            DatasetItemDto item = new() { Id = Guid.NewGuid(), Title = "mine", Description = "old", Tags = new List<string> { "x" }, Version = "5" };
            (ItemEditService service, DatasetState state, _) = Build(
                _ => Json(HttpStatusCode.PreconditionFailed, new ItemEditResponse
                {
                    ItemId = item.Id,
                    Version = "6",
                    Title = "theirs",
                    Message = "changed",
                }),
                item);

            ItemUpdateOutcome outcome = await service.UpdateItemAsync(item, title: "mine, edited");

            Assert.Equal(ItemUpdateOutcome.Conflict, outcome);
            DatasetItemDto stored = state.Items.Single();
            Assert.Equal("theirs", stored.Title);
            // Fields the server doesn't hold (a streamed item's unedited ones) keep the source's value.
            Assert.Equal("old", stored.Description);
            Assert.Equal(new[] { "x" }, stored.Tags);
            Assert.Equal("6", stored.Version);
        }

        [Fact]
        public async Task BulkUpdate_SaysHowManyItemsCouldNotBeEditedAndWhy()
        {
            Guid datasetId = Guid.NewGuid();
            DatasetItemDto a = new() { Id = Guid.NewGuid(), DatasetId = datasetId, Tags = new List<string> { "t" }, Version = "1" };
            DatasetItemDto b = new() { Id = Guid.NewGuid(), DatasetId = datasetId, Version = "1" };
            DatasetItemDto c = new() { Id = Guid.NewGuid(), DatasetId = datasetId, Version = "1" };
            (ItemEditService service, DatasetState state, StubHandler handler) = Build(
                _ => Json(HttpStatusCode.OK, new BulkUpdateItemsResponse
                {
                    UpdatedCount = 1,
                    Skipped = new List<BulkSkippedItem>
                    {
                        new() { ItemId = b.Id, Reason = BulkSkipReasons.Forbidden },
                        new() { ItemId = c.Id, Reason = BulkSkipReasons.NotFound },
                    },
                    Versions = new Dictionary<Guid, string> { [a.Id] = "2" },
                }),
                a, b, c);

            BulkEditResult result = await service.BulkUpdateAsync(new[] { a, b, c }, tagsToAdd: new List<string> { "new" });

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.UpdatedCount);
            Assert.Equal("Updated 1 of 3 items. 2 couldn't be edited: 1 not yours to edit, 1 not found.", result.Summary);
            // The edited item takes the change and its new version; the skipped ones are untouched.
            Assert.Equal(new[] { "t", "new" }, state.Items.Single(i => i.Id == a.Id).Tags);
            Assert.Equal("2", state.Items.Single(i => i.Id == a.Id).Version);
            Assert.Empty(state.Items.Single(i => i.Id == b.Id).Tags);
            // A tag change sends the tags shown, which a HuggingFace streaming item needs.
            BulkUpdateItemsRequest sent = System.Text.Json.JsonSerializer.Deserialize<BulkUpdateItemsRequest>(handler.Bodies[0], new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(datasetId, sent.DatasetId);
            Assert.Equal(new[] { "t" }, sent.CurrentTags![a.Id]);
        }

        /// <summary>
        /// Found in the browser check: a card hands over the copy it was rendered with, so after an
        /// edit the detail panel showed the old values and its next edit sent the old version (412).
        /// Selecting takes the loaded copy, and an edit tells the grid to re-render.
        /// </summary>
        [Fact]
        public void SelectingAStaleCopy_SelectsTheEditedItem()
        {
            DatasetItemDto loaded = new() { Id = Guid.NewGuid(), Title = "before", Version = "0" };
            DatasetState state = new();
            state.LoadDataset(new Dataset { Id = "ds", Name = "test" }, new List<DatasetItemDto> { loaded });
            int editsBefore = state.ItemEditVersion;

            state.UpdateItem(loaded with { Title = "after", Version = "1" });
            state.SelectItem(loaded);

            Assert.Equal("after", state.SelectedItem!.Title);
            Assert.Equal("1", state.SelectedItem.Version);
            Assert.NotEqual(editsBefore, state.ItemEditVersion);
        }

        [Fact]
        public void BulkSummary_WithNothingSkipped_IsJustTheCount()
        {
            Assert.Equal("Updated 3 items.", BulkEditResult.Describe(new BulkUpdateItemsResponse { UpdatedCount = 3 }, 3));
        }

        [Fact]
        public async Task BulkUpdate_WhenTheRequestFails_SaysSo()
        {
            DatasetItemDto a = new() { Id = Guid.NewGuid() };
            (ItemEditService service, _, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), a);

            BulkEditResult result = await service.BulkUpdateAsync(new[] { a }, setFavorite: true);

            Assert.False(result.Succeeded);
            Assert.Equal("Couldn't edit the 1 item. Try again.", result.Summary);
        }
    }
}
