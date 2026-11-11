using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Behaviour of the list and item endpoints for datasets whose items live in Parquet (uploaded
    /// or ingested locally): the create response carries the persisted timestamps (audit D-1), page
    /// sizes are bounded (D-3), the cached total is reported (G-1), and malformed cursors are rejected
    /// instead of silently restarting the read (G-3).
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class LocalDatasetApiTests : IAsyncLifetime
    {
        /// <summary>Largest page the server serves. Mirrors PageLimits.MaxPageSize.</summary>
        private const int MaxPageSize = 100;

        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;

        public LocalDatasetApiTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();

            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("local-api-test-user", "local-api-test-user@example.com", "password123"));
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        /// <summary>Creates a local dataset owned by the test user, with the given number of items written to Parquet.</summary>
        private async Task<DatasetDto> CreateLocalDatasetAsync(int itemCount, long totalItems)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("local-" + Guid.NewGuid().ToString("N")[..8], null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == created.Id);
            entity.TotalItems = totalItems;
            await db.SaveChangesAsync();

            if (itemCount > 0)
            {
                IDatasetItemRepository itemRepository = scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>();
                List<DatasetItemDto> items = new();
                for (int i = 0; i < itemCount; i++)
                {
                    items.Add(new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = created.Id,
                        ExternalId = $"row-{i}",
                        Title = $"item {i}",
                    });
                }
                await itemRepository.AddRangeAsync(created.Id, items);
            }

            return created;
        }

        [Fact]
        public async Task CreateDataset_ResponseCarriesPersistedTimestamps()
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("stamped-" + Guid.NewGuid().ToString("N")[..8], null));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            DatasetDto created = (await response.Content.ReadFromJsonAsync<DatasetDto>())!;
            created.CreatedAt.Should().BeAfter(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            created.UpdatedAt.Should().BeAfter(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public async Task GetItems_PageSizeAboveMaximum_IsClampedToMaxPageSize()
        {
            DatasetDto dataset = await CreateLocalDatasetAsync(itemCount: 150, totalItems: 150);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=1000000");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            PageResponse<DatasetItemDto> page = (await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!;
            page.Items.Should().HaveCount(MaxPageSize);
            page.NextCursor.Should().NotBeNull("more items remain after the clamped page");
        }

        [Fact]
        public async Task GetAllDatasets_PageSizeAboveMaximum_IsClampedAndEchoed()
        {
            HttpResponseMessage response = await _client.GetAsync("/api/datasets?page=0&pageSize=1000000");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(MaxPageSize);
        }

        [Fact]
        public async Task GetItems_LocalDataset_ReportsCachedTotalCount()
        {
            DatasetDto dataset = await CreateLocalDatasetAsync(itemCount: 3, totalItems: 3);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            PageResponse<DatasetItemDto> page = (await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!;
            page.TotalCount.Should().Be(3);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-1:0")]
        [InlineData("1:x")]
        [InlineData("0")]
        [InlineData("1:2:3")]
        public async Task GetItems_MalformedLocalCursor_Returns400InsteadOfRestarting(string cursor)
        {
            DatasetDto dataset = await CreateLocalDatasetAsync(itemCount: 3, totalItems: 3);

            HttpResponseMessage response = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/items?pageSize=10&cursor={Uri.EscapeDataString(cursor)}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetItems_ValidLocalCursor_IsAcceptedAndResumesPaging()
        {
            DatasetDto dataset = await CreateLocalDatasetAsync(itemCount: 150, totalItems: 150);

            HttpResponseMessage first = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=100");
            PageResponse<DatasetItemDto> firstPage = (await first.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!;

            HttpResponseMessage second = await _client.GetAsync(
                $"/api/datasets/{dataset.Id}/items?pageSize=100&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");

            second.StatusCode.Should().Be(HttpStatusCode.OK);
            PageResponse<DatasetItemDto> secondPage = (await second.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!;
            secondPage.Items.Should().HaveCount(50);
        }
    }
}
