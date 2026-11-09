using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 4.4 (audit G-2, U-6): the items endpoint filters. Local datasets apply every filter
    /// through the Parquet reader, HuggingFace streaming datasets search through datasets-server,
    /// and anything a source can't do is a 400 rather than a silently unfiltered page.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class ItemFilteringApiTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private readonly Mock<IHuggingFaceDatasetServerClient> _hf = new();
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;

        public ItemFilteringApiTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigureTestServices = services => services.AddSingleton(_hf.Object);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("filter-test-user", "filter-test-user@example.com", "password123"));
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        private async Task<DatasetDetailDto> CreateDatasetAsync(Action<DatasetEntity>? configure = null)
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("filter-" + Guid.NewGuid().ToString("N")[..8], null));
            DatasetDetailDto created = (await response.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            if (configure is not null)
            {
                using IServiceScope scope = _factory.Services.CreateScope();
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == created.Id);
                configure(entity);
                await db.SaveChangesAsync();
            }
            return created;
        }

        /// <summary>A local dataset of 60 items: every third is tagged "cat", every fifth a favourite, widths 100..690.</summary>
        private async Task<DatasetDetailDto> CreateLocalDatasetAsync()
        {
            DatasetDetailDto dataset = await CreateDatasetAsync(e => e.TotalItems = 60);
            using IServiceScope scope = _factory.Services.CreateScope();
            IDatasetItemRepository items = scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>();
            await items.AddRangeAsync(dataset.Id, Enumerable.Range(0, 60).Select(i => new DatasetItemDto
            {
                Id = Guid.NewGuid(),
                DatasetId = dataset.Id,
                ExternalId = $"row-{i:D3}",
                Title = i == 42 ? "Sunset over the harbour" : $"item {i}",
                Width = 100 + (i * 10),
                Height = 100,
                Tags = i % 3 == 0 ? new List<string> { "cat" } : new List<string> { "dog" },
                IsFavorite = i % 5 == 0,
                Metadata = new Dictionary<string, string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).ToList());
            return dataset;
        }

        private async Task<(List<string> Ids, List<PageResponse<DatasetItemDto>> Pages)> ReadAllAsync(Guid datasetId, string filterQuery, int pageSize = 7)
        {
            List<string> ids = new();
            List<PageResponse<DatasetItemDto>> pages = new();
            string? cursor = null;
            do
            {
                string url = $"/api/datasets/{datasetId}/items?pageSize={pageSize}{filterQuery}" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
                HttpResponseMessage response = await _client.GetAsync(url);
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                PageResponse<DatasetItemDto> page = (await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!;
                pages.Add(page);
                ids.AddRange(page.Items.Select(i => i.ExternalId!));
                cursor = page.NextCursor;
            }
            while (cursor is not null && pages.Count < 100);
            return (ids, pages);
        }

        [Fact]
        public async Task LocalDataset_TagFilter_PagesThroughEveryMatchOnce()
        {
            DatasetDetailDto dataset = await CreateLocalDatasetAsync();

            (List<string> ids, List<PageResponse<DatasetItemDto>> pages) = await ReadAllAsync(dataset.Id, "&tag=cat");

            ids.Should().Equal(Enumerable.Range(0, 60).Where(i => i % 3 == 0).Select(i => $"row-{i:D3}"));
            pages.Should().OnlyContain(p => p.TotalCount == null, "a filtered page doesn't know its match count");
        }

        [Fact]
        public async Task LocalDataset_CombinedFilters_ApplyTogether()
        {
            DatasetDetailDto dataset = await CreateLocalDatasetAsync();

            (List<string> ids, _) = await ReadAllAsync(dataset.Id, "&favorites=true&minWidth=300&maxWidth=500");

            ids.Should().Equal(new[] { 20, 25, 30, 35, 40 }.Select(i => $"row-{i:D3}"));
        }

        [Fact]
        public async Task LocalDataset_Search_MatchesTitles()
        {
            DatasetDetailDto dataset = await CreateLocalDatasetAsync();

            (List<string> ids, _) = await ReadAllAsync(dataset.Id, "&q=HARBOUR");

            ids.Should().Equal("row-042");
        }

        [Fact]
        public async Task LocalDataset_NoFilter_StillReportsTheTotal()
        {
            DatasetDetailDto dataset = await CreateLocalDatasetAsync();

            PageResponse<DatasetItemDto> page = (await _client.GetFromJsonAsync<PageResponse<DatasetItemDto>>($"/api/datasets/{dataset.Id}/items?pageSize=10"))!;

            page.TotalCount.Should().Be(60);
            page.Items.Should().HaveCount(10);
        }

        [Theory]
        [InlineData("&minWidth=wide")]
        [InlineData("&favorites=maybe")]
        [InlineData("&dateFrom=yesterday")]
        public async Task MalformedFilter_IsRejected(string query)
        {
            DatasetDetailDto dataset = await CreateLocalDatasetAsync();

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task S3Dataset_AnyFilter_IsRejected()
        {
            DatasetDetailDto dataset = await CreateDatasetAsync(e =>
            {
                e.SourceType = DatasetSourceType.ExternalS3Streaming;
                e.IsStreaming = true;
            });

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?q=cat");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task HuggingFaceDataset_Search_UsesDatasetsServerSearch_AndOtherFiltersAreRejected()
        {
            DatasetDetailDto dataset = await CreateDatasetAsync(e =>
            {
                e.SourceType = DatasetSourceType.HuggingFaceStreaming;
                e.IsStreaming = true;
                e.HuggingFaceRepository = "org/ds";
                e.HuggingFaceConfig = "default";
                e.HuggingFaceSplit = "train";
                e.TotalItems = 1000;
            });
            _hf.Setup(c => c.SearchRowsAsync("org/ds", "default", "train", "red bike", 0, 10, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceRowsPage { Dataset = "org/ds", Config = "default", Split = "train", NumRowsTotal = 3, Rows = new List<HuggingFaceRow>() });

            HttpResponseMessage search = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10&q=red%20bike");
            HttpResponseMessage tagged = await _client.GetAsync($"/api/datasets/{dataset.Id}/items?pageSize=10&tag=cat");

            search.StatusCode.Should().Be(HttpStatusCode.OK);
            (await search.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>())!.TotalCount.Should().Be(3);
            _hf.Verify(c => c.GetRowsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            tagged.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using IServiceScope scope = _factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>().Datasets.SingleAsync(d => d.Id == dataset.Id))
                .TotalItems.Should().Be(1000, "a search's match count must not overwrite the dataset's size");
        }

        /// <summary>The query string the client builds parses back to the same filter on the server.</summary>
        [Fact]
        public void FilterQueryString_RoundTrips()
        {
            FilterRequest filter = new()
            {
                SearchQuery = "red & blue",
                Tags = new[] { "cat", "big cat" },
                FavoritesOnly = true,
                MinWidth = 10,
                MaxHeight = 900,
                DateFrom = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            };

            Dictionary<string, Microsoft.Extensions.Primitives.StringValues> parsed = QueryHelpers.ParseQuery("?x=1" + FilterQueryString.Build(filter));
            (FilterRequest? back, string? error) = ItemFilterQuery.Parse(new QueryCollection(parsed));

            error.Should().BeNull();
            back.Should().BeEquivalentTo(filter);
        }
    }
}
