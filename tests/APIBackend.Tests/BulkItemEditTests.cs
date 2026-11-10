using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit E-1: a bulk edit skipped items it couldn't edit and only returned a lower count, so a
    /// client could not tell which items were left unchanged, or whether they were missing or denied.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class BulkItemEditTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public BulkItemEditTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DisposeAsync();
            await _factory.DropDatabaseAsync();
        }

        private async Task<HttpClient> SignUpAsync(string username)
        {
            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(username, $"{username}@example.com", "password123"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return client;
        }

        /// <summary>Creates a local dataset as <paramref name="client"/>'s user with items of the given ids.</summary>
        private async Task<Guid> CreateDatasetAsync(HttpClient client, bool isPublic, params Guid[] itemIds)
        {
            DatasetDetailDto dataset = (await (await client.PostAsJsonAsync("/api/datasets",
                    new CreateDatasetRequest("bulk-" + Guid.NewGuid().ToString("N")[..8], null, IsPublic: isPublic)))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            using IServiceScope scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>().AddRangeAsync(dataset.Id,
                itemIds.Select(id => new DatasetItemDto { Id = id, DatasetId = dataset.Id, Title = "item", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }));
            return dataset.Id;
        }

        [Fact]
        public async Task BulkEdit_ReportsSkippedItemsWithReasons()
        {
            HttpClient owner = await SignUpAsync("bulk-owner");
            HttpClient other = await SignUpAsync("bulk-other");
            Guid mine1 = Guid.NewGuid(), mine2 = Guid.NewGuid(), theirsPublic = Guid.NewGuid(), theirsPrivate = Guid.NewGuid(), missing = Guid.NewGuid();
            Guid myDataset = await CreateDatasetAsync(owner, isPublic: false, mine1, mine2);
            await CreateDatasetAsync(other, isPublic: true, theirsPublic);
            await CreateDatasetAsync(other, isPublic: false, theirsPrivate);

            HttpResponseMessage response = await owner.PatchAsJsonAsync("/api/items/bulk", new BulkUpdateItemsRequest
            {
                ItemIds = new List<Guid> { mine1, mine2, theirsPublic, theirsPrivate, missing },
                TagsToAdd = new List<string> { "reviewed" },
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            BulkUpdateItemsResponse body = (await response.Content.ReadFromJsonAsync<BulkUpdateItemsResponse>())!;
            body.UpdatedCount.Should().Be(2);
            body.Skipped.Should().BeEquivalentTo(new[]
            {
                new BulkSkippedItem { ItemId = theirsPublic, Reason = BulkSkipReasons.Forbidden },
                // A private dataset of someone else reads as not-found, so its items aren't revealed.
                new BulkSkippedItem { ItemId = theirsPrivate, Reason = BulkSkipReasons.NotFound },
                new BulkSkippedItem { ItemId = missing, Reason = BulkSkipReasons.NotFound },
            });
            body.Versions.Keys.Should().BeEquivalentTo(new[] { mine1, mine2 });

            PageResponse<DatasetItemDto>? page = await owner.GetFromJsonAsync<PageResponse<DatasetItemDto>>($"/api/datasets/{myDataset}/items?pageSize=10");
            page!.Items.Should().OnlyContain(i => i.Tags.Contains("reviewed"));
            page.Items.Select(i => i.Version).Should().BeEquivalentTo(new[] { body.Versions[mine1], body.Versions[mine2] });
        }

        /// <summary>
        /// PR 6.1 review: the response carries each edited item's values as stored, including a
        /// change another user made first, so the client can take them instead of recomputing.
        /// </summary>
        [Fact]
        public async Task BulkEdit_ReturnsEachEditedItemsStoredValues()
        {
            HttpClient owner = await SignUpAsync("bulk-values");
            Guid itemId = Guid.NewGuid();
            Guid dataset = await CreateDatasetAsync(owner, isPublic: false, itemId);
            (await owner.PatchAsJsonAsync($"/api/items/{itemId}", new UpdateItemRequest { DatasetId = dataset, Tags = new List<string> { "a", "b" } }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage response = await owner.PatchAsJsonAsync("/api/items/bulk", new BulkUpdateItemsRequest
            {
                ItemIds = new List<Guid> { itemId },
                DatasetId = dataset,
                TagsToAdd = new List<string> { "c" },
            });

            BulkUpdateItemsResponse body = (await response.Content.ReadFromJsonAsync<BulkUpdateItemsResponse>())!;
            ItemEditResponse stored = body.Items.Should().ContainSingle().Subject;
            stored.ItemId.Should().Be(itemId);
            stored.Tags.Should().Equal("a", "b", "c");
            stored.Version.Should().Be(body.Versions[itemId]);
        }

        [Fact]
        public async Task BulkEdit_WithDatasetId_ReportsIdsNotInThatDatasetAsNotFound()
        {
            HttpClient owner = await SignUpAsync("bulk-scoped");
            Guid inside = Guid.NewGuid(), elsewhere = Guid.NewGuid();
            Guid dataset = await CreateDatasetAsync(owner, isPublic: false, inside);
            await CreateDatasetAsync(owner, isPublic: false, elsewhere);

            HttpResponseMessage response = await owner.PatchAsJsonAsync("/api/items/bulk", new BulkUpdateItemsRequest
            {
                ItemIds = new List<Guid> { inside, elsewhere },
                DatasetId = dataset,
                SetFavorite = true,
            });

            BulkUpdateItemsResponse body = (await response.Content.ReadFromJsonAsync<BulkUpdateItemsResponse>())!;
            body.UpdatedCount.Should().Be(1);
            body.Skipped.Should().ContainSingle().Which.Should().Be(new BulkSkippedItem { ItemId = elsewhere, Reason = BulkSkipReasons.NotFound });
        }
    }
}
