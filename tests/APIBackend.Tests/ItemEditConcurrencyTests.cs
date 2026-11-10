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
    /// Audit E-4: a local item edit read the item, changed it outside any lock and wrote the whole
    /// item back, so of two concurrent edits only the last survived, and there was no version to
    /// notice an edit made from a stale copy. Edits now apply to the current item inside the
    /// dataset's lock, and PATCH honours If-Match.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class ItemEditConcurrencyTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;

        public ItemEditConcurrencyTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
            HttpResponseMessage registered = await _client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("edit-concurrency", "edit-concurrency@example.com", "password123"));
            registered.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
            await _factory.DropDatabaseAsync();
        }

        /// <summary>Creates a local dataset holding one item and returns both ids.</summary>
        private async Task<(Guid DatasetId, Guid ItemId)> CreateItemAsync()
        {
            DatasetDetailDto dataset = (await (await _client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("edits-" + Guid.NewGuid().ToString("N")[..8], null)))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            Guid itemId = Guid.NewGuid();
            using IServiceScope scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>().AddRangeAsync(dataset.Id, new[]
            {
                new DatasetItemDto
                {
                    Id = itemId,
                    DatasetId = dataset.Id,
                    Title = "original title",
                    Description = "original description",
                    ImageUrl = "https://example.com/a.jpg",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            });
            return (dataset.Id, itemId);
        }

        private async Task<DatasetItemDto> GetItemAsync(Guid datasetId, Guid itemId)
        {
            PageResponse<DatasetItemDto>? page = await _client.GetFromJsonAsync<PageResponse<DatasetItemDto>>($"/api/datasets/{datasetId}/items?pageSize=10");
            return page!.Items.Single(i => i.Id == itemId);
        }

        private Task<HttpResponseMessage> PatchAsync(Guid itemId, UpdateItemRequest request, string? ifMatch = null)
        {
            HttpRequestMessage message = new(HttpMethod.Patch, $"/api/items/{itemId}") { Content = JsonContent.Create(request) };
            if (ifMatch is not null)
            {
                message.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
            return _client.SendAsync(message);
        }

        [Fact]
        public async Task ConcurrentEditsOfDifferentFields_BothSurvive()
        {
            (Guid datasetId, Guid itemId) = await CreateItemAsync();

            // Several rounds, because the lost update needs the two reads to land before either write.
            for (int round = 0; round < 8; round++)
            {
                HttpResponseMessage[] responses = await Task.WhenAll(
                    PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, Title = $"title {round}" }),
                    PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, Description = $"description {round}" }));
                responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);

                DatasetItemDto stored = await GetItemAsync(datasetId, itemId);
                stored.Title.Should().Be($"title {round}", $"round {round} must keep the title edit");
                stored.Description.Should().Be($"description {round}", $"round {round} must keep the description edit");
            }
        }

        [Fact]
        public async Task StaleIfMatch_Gets412WithTheCurrentItem_AndAMatchingOneSucceeds()
        {
            (Guid datasetId, Guid itemId) = await CreateItemAsync();
            string loaded = (await GetItemAsync(datasetId, itemId)).Version!;
            loaded.Should().NotBeNullOrEmpty();

            // Someone else edits first, from the same copy.
            HttpResponseMessage first = await PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, Title = "theirs" }, $"\"{loaded}\"");
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            ItemEditResponse firstBody = (await first.Content.ReadFromJsonAsync<ItemEditResponse>())!;
            firstBody.Version.Should().NotBe(loaded);
            first.Headers.ETag!.Tag.Should().Be($"\"{firstBody.Version}\"");

            // The stale edit is refused and nothing is written; the body carries the current item.
            HttpResponseMessage stale = await PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, Title = "mine" }, $"\"{loaded}\"");
            stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            ItemEditResponse staleBody = (await stale.Content.ReadFromJsonAsync<ItemEditResponse>())!;
            staleBody.Title.Should().Be("theirs");
            staleBody.Version.Should().Be(firstBody.Version);
            staleBody.Message.Should().NotBeNullOrWhiteSpace();
            (await GetItemAsync(datasetId, itemId)).Title.Should().Be("theirs");

            // The version from the response is the one a page read reports, and it is accepted.
            (await GetItemAsync(datasetId, itemId)).Version.Should().Be(firstBody.Version);
            HttpResponseMessage retried = await PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, Title = "mine" }, $"\"{firstBody.Version}\"");
            retried.StatusCode.Should().Be(HttpStatusCode.OK);
            (await GetItemAsync(datasetId, itemId)).Title.Should().Be("mine");
        }

        [Fact]
        public async Task EditWithoutIfMatch_IsStillAccepted()
        {
            (Guid datasetId, Guid itemId) = await CreateItemAsync();

            HttpResponseMessage response = await PatchAsync(itemId, new UpdateItemRequest { DatasetId = datasetId, IsFavorite = true });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.ETag.Should().NotBeNull();
            (await GetItemAsync(datasetId, itemId)).IsFavorite.Should().BeTrue();
        }
    }
}
