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

        /// <summary>A page of rows with the given indexes, each with a text caption.</summary>
        private static HuggingFaceRowsPage RowsPage(long numRowsTotal, params long[] rowIndexes)
        {
            List<HuggingFaceRow> rows = new();
            foreach (long index in rowIndexes)
            {
                rows.Add(new HuggingFaceRow
                {
                    RowIndex = index,
                    Columns = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["text"] = System.Text.Json.JsonSerializer.SerializeToElement($"caption {index}"),
                        ["tags"] = System.Text.Json.JsonSerializer.SerializeToElement("source-a, source-b"),
                    },
                });
            }
            return new HuggingFaceRowsPage { Dataset = Repo, Config = "default", Split = "train", NumRowsTotal = numRowsTotal, Rows = rows };
        }

        private async Task<List<DatasetItemDto>> GetItemsAsync(Guid datasetId, string query = "")
        {
            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{datasetId}/items?pageSize=10{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetStudio.DTO.Common.PageResponse<DatasetItemDto>? page =
                await response.Content.ReadFromJsonAsync<DatasetStudio.DTO.Common.PageResponse<DatasetItemDto>>();
            return page!.Items.ToList();
        }

        /// <summary>
        /// Audit E-3: each page request gave every row a new Guid, so an id taken from one page
        /// was unknown by the next request and edits always 404'd. A search result for the same
        /// row (datasets-server reports its original row_idx) must carry the same id as well.
        /// </summary>
        [Fact]
        public async Task GetItems_SameRow_HasTheSameIdAcrossRequestsAndSearch()
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train");
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => RowsPage(3, 0, 1, 2));
            _hfServerClientMock
                .Setup(c => c.SearchRowsAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => RowsPage(1, 1));

            List<DatasetItemDto> first = await GetItemsAsync(dataset.Id);
            List<DatasetItemDto> second = await GetItemsAsync(dataset.Id);
            List<DatasetItemDto> found = await GetItemsAsync(dataset.Id, "&q=caption");

            first.Select(i => i.Id).Should().OnlyHaveUniqueItems().And.Equal(second.Select(i => i.Id));
            found.Should().ContainSingle().Which.Id.Should().Be(first[1].Id);
        }

        /// <summary>A streaming dataset serving rows 0..2 on every page request.</summary>
        private async Task<DatasetDetailDto> CreateDatasetWithRowsAsync()
        {
            DatasetDetailDto dataset = await CreateStreamingDatasetAsync(split: "train");
            _hfServerClientMock
                .Setup(c => c.GetRowsAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => RowsPage(3, 0, 1, 2));
            return dataset;
        }

        private Task<HttpResponseMessage> PatchAsync(Guid itemId, DatasetStudio.DTO.Items.UpdateItemRequest request, string? ifMatch = null)
        {
            HttpRequestMessage message = new(HttpMethod.Patch, $"/api/items/{itemId}") { Content = JsonContent.Create(request) };
            if (ifMatch is not null)
            {
                message.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
            return _client.SendAsync(message);
        }

        /// <summary>
        /// Audit E-3: edits of streamed rows 404'd (their ids changed per request and nothing could
        /// store an edit). They are now kept in an overlay and applied to every page served.
        /// </summary>
        [Fact]
        public async Task EditStreamingRow_PersistsAndShowsOnTheNextPage()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            List<DatasetItemDto> before = await GetItemsAsync(dataset.Id);
            before.Should().OnlyContain(i => i.Version == "0");

            HttpResponseMessage response = await PatchAsync(before[1].Id, new DatasetStudio.DTO.Items.UpdateItemRequest
            {
                DatasetId = dataset.Id,
                Title = "edited title",
                Tags = new List<string> { "mine" },
                IsFavorite = true,
                Metadata = new Dictionary<string, string> { ["note"] = "checked" },
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetStudio.DTO.Items.ItemEditResponse body = (await response.Content.ReadFromJsonAsync<DatasetStudio.DTO.Items.ItemEditResponse>())!;
            body.Version.Should().Be("1");
            body.Title.Should().Be("edited title");
            body.Description.Should().BeNull("the description was not edited and comes from the source");
            response.Headers.ETag!.Tag.Should().Be("\"1\"");

            List<DatasetItemDto> after = await GetItemsAsync(dataset.Id);
            DatasetItemDto edited = after.Single(i => i.Id == before[1].Id);
            edited.Title.Should().Be("edited title");
            edited.Description.Should().Be(before[1].Description);
            edited.Tags.Should().Equal("mine");
            edited.IsFavorite.Should().BeTrue();
            edited.Metadata.Should().Contain("note", "checked").And.ContainKey("hf_repository");
            edited.Version.Should().Be("1");
            after.Where(i => i.Id != edited.Id).Should().BeEquivalentTo(before.Where(i => i.Id != edited.Id),
                o => o.Excluding(i => i.CreatedAt).Excluding(i => i.UpdatedAt));
        }

        [Fact]
        public async Task EditStreamingRow_StaleIfMatch_Gets412_AndMatchingOneSucceeds()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            DatasetItemDto item = (await GetItemsAsync(dataset.Id))[0];

            (await PatchAsync(item.Id, new() { DatasetId = dataset.Id, Title = "theirs" }, "\"0\"")).StatusCode.Should().Be(HttpStatusCode.OK);
            HttpResponseMessage stale = await PatchAsync(item.Id, new() { DatasetId = dataset.Id, Description = "mine" }, "\"0\"");

            stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            DatasetStudio.DTO.Items.ItemEditResponse current = (await stale.Content.ReadFromJsonAsync<DatasetStudio.DTO.Items.ItemEditResponse>())!;
            current.Version.Should().Be("1");
            current.Title.Should().Be("theirs");
            (await GetItemsAsync(dataset.Id))[0].Description.Should().Be(item.Description, "the refused edit wrote nothing");

            (await PatchAsync(item.Id, new() { DatasetId = dataset.Id, Description = "mine" }, "\"1\"")).StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetItemDto final = (await GetItemsAsync(dataset.Id))[0];
            final.Title.Should().Be("theirs");
            final.Description.Should().Be("mine");
            final.Version.Should().Be("2");
        }

        [Fact]
        public async Task EditStreamingRow_ConcurrentEditsOfDifferentFields_BothSurvive()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            Guid itemId = (await GetItemsAsync(dataset.Id))[2].Id;

            HttpResponseMessage[] responses = await Task.WhenAll(
                PatchAsync(itemId, new() { DatasetId = dataset.Id, Title = "a title" }),
                PatchAsync(itemId, new() { DatasetId = dataset.Id, Description = "a description" }),
                PatchAsync(itemId, new() { DatasetId = dataset.Id, IsFavorite = true }));

            responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
            DatasetItemDto stored = (await GetItemsAsync(dataset.Id))[2];
            stored.Title.Should().Be("a title");
            stored.Description.Should().Be("a description");
            stored.IsFavorite.Should().BeTrue();
            stored.Version.Should().Be("3");
        }

        [Fact]
        public async Task EditStreamingRow_WithoutDatasetId_Is404SayingWhatToSend()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            DatasetItemDto item = (await GetItemsAsync(dataset.Id))[0];

            HttpResponseMessage response = await PatchAsync(item.Id, new() { Title = "x" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("datasetId");
        }

        /// <summary>
        /// A bulk tag change on a streamed row starts from the tags the client shows (the server can't
        /// read the row back from its id); a row without them is reported, not given a wrong list.
        /// </summary>
        [Fact]
        public async Task BulkEditStreamingRows_StartsFromShownTags_AndReportsRowsWithout()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            List<DatasetItemDto> items = await GetItemsAsync(dataset.Id);
            items[0].Tags.Should().Equal("source-a", "source-b");

            HttpResponseMessage response = await _client.PatchAsJsonAsync("/api/items/bulk", new DatasetStudio.DTO.Items.BulkUpdateItemsRequest
            {
                DatasetId = dataset.Id,
                ItemIds = new List<Guid> { items[0].Id, items[1].Id },
                TagsToAdd = new List<string> { "added" },
                TagsToRemove = new List<string> { "source-b" },
                CurrentTags = new Dictionary<Guid, List<string>> { [items[0].Id] = items[0].Tags },
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetStudio.DTO.Items.BulkUpdateItemsResponse body = (await response.Content.ReadFromJsonAsync<DatasetStudio.DTO.Items.BulkUpdateItemsResponse>())!;
            body.UpdatedCount.Should().Be(1);
            body.Versions.Should().Equal(new Dictionary<Guid, string> { [items[0].Id] = "1" });
            body.Skipped.Should().ContainSingle().Which.Should().Be(
                new DatasetStudio.DTO.Items.BulkSkippedItem { ItemId = items[1].Id, Reason = DatasetStudio.DTO.Items.BulkSkipReasons.TagsUnknown });

            List<DatasetItemDto> after = await GetItemsAsync(dataset.Id);
            after[0].Tags.Should().Equal("source-a", "added");
            after[1].Tags.Should().Equal("source-a", "source-b");

            // Once the server holds an item's tags it no longer needs the client's.
            HttpResponseMessage again = await _client.PatchAsJsonAsync("/api/items/bulk", new DatasetStudio.DTO.Items.BulkUpdateItemsRequest
            {
                DatasetId = dataset.Id,
                ItemIds = new List<Guid> { items[0].Id },
                TagsToAdd = new List<string> { "second" },
            });
            (await again.Content.ReadFromJsonAsync<DatasetStudio.DTO.Items.BulkUpdateItemsResponse>())!.UpdatedCount.Should().Be(1);
            (await GetItemsAsync(dataset.Id))[0].Tags.Should().Equal("source-a", "added", "second");
        }

        /// <summary>A dataset's overlays go with it (foreign key cascade), so nothing is left behind.</summary>
        [Fact]
        public async Task DeleteDataset_RemovesItsOverlays()
        {
            DatasetDetailDto dataset = await CreateDatasetWithRowsAsync();
            DatasetItemDto item = (await GetItemsAsync(dataset.Id))[0];
            (await PatchAsync(item.Id, new() { DatasetId = dataset.Id, Title = "to be deleted" })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await CountOverlaysAsync(dataset.Id)).Should().Be(1);

            (await _client.DeleteAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await CountOverlaysAsync(dataset.Id)).Should().Be(0);
        }

        private async Task<int> CountOverlaysAsync(Guid datasetId)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            return await db.ItemOverlays.CountAsync(o => o.DatasetId == datasetId);
        }
    }
}
