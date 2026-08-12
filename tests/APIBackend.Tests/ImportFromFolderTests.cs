using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// End-to-end coverage for local-folder ingestion: DatasetIngestionService.
    /// IngestFromFolderAsync existed with zero call sites before this endpoint was wired up
    /// (Phase 1), and even then its items pointed at file:// URIs a browser can never fetch —
    /// this proves both the route and the served-image round trip actually work, not just
    /// that ingestion "completes" per the status field.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class ImportFromFolderTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private string? _sourceFolder;

        public ImportFromFolderTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
            if (_sourceFolder is not null && Directory.Exists(_sourceFolder))
            {
                Directory.Delete(_sourceFolder, recursive: true);
            }
        }

        [Fact]
        public async Task ImportFromFolder_IngestsImages_AndTheyAreActuallyServeable()
        {
            _sourceFolder = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "import-folder-source", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sourceFolder);
            byte[] fakeImageBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03 }; // JPEG-ish magic bytes, content doesn't matter for this test
            await File.WriteAllBytesAsync(Path.Combine(_sourceFolder, "photo-1.jpg"), fakeImageBytes);
            await File.WriteAllBytesAsync(Path.Combine(_sourceFolder, "photo-2.png"), fakeImageBytes);

            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("folder-import-test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            HttpResponseMessage importResponse = await _client.PostAsJsonAsync(
                $"/api/datasets/{created.Id}/import-folder", new ImportFolderRequest(_sourceFolder));
            importResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            IngestionStatusDto finalStatus = IngestionStatusDto.Pending;
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(200);
                DatasetDetailDto polled = (await (await _client.GetAsync($"/api/datasets/{created.Id}"))
                    .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
                finalStatus = polled.Status;
                if (finalStatus is IngestionStatusDto.Completed or IngestionStatusDto.Failed)
                {
                    break;
                }
            }
            finalStatus.Should().Be(IngestionStatusDto.Completed);

            HttpResponseMessage itemsResponse = await _client.GetAsync($"/api/datasets/{created.Id}/items");
            PageResponse<DatasetItemDto>? page = await itemsResponse.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>();
            page.Should().NotBeNull();
            page!.Items.Should().HaveCount(2);

            foreach (DatasetItemDto item in page.Items)
            {
                item.ImageUrl.Should().NotBeNull();
                item.ImageUrl.Should().StartWith($"/api/datasets/{created.Id}/files/");
                item.ImageUrl.Should().NotContain("file://");

                // The real proof: fetching the URL the item claims to be servable at actually
                // returns the image bytes, not a 404 — this is what file:// URLs, or a
                // deleted-after-extraction temp folder, would fail at.
                HttpResponseMessage fileResponse = await _client.GetAsync(item.ImageUrl);
                fileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                byte[] servedBytes = await fileResponse.Content.ReadAsByteArrayAsync();
                servedBytes.Should().Equal(fakeImageBytes);
            }
        }

        // Regression test: UpdateItemCountAsync used to be called with just the current
        // batch's item count, so a second ingestion into an existing dataset overwrote
        // TotalItems with only the new batch's size instead of the true cumulative total —
        // the dataset's displayed/reported count would silently understate how much data it
        // actually has. Importing the same folder twice into one dataset is exactly the
        // condition that exercises this (mirrors the live re-upload check this plan calls for).
        [Fact]
        public async Task ImportFromFolder_CalledTwice_ReportsCumulativeTotalItems()
        {
            _sourceFolder = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "import-folder-source", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sourceFolder);
            byte[] fakeImageBytes = { 0xFF, 0xD8, 0xFF, 0xE0 };
            await File.WriteAllBytesAsync(Path.Combine(_sourceFolder, "only-photo.jpg"), fakeImageBytes);

            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("cumulative-count-test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            async Task<IngestionStatusDto> ImportAndWaitAsync()
            {
                await _client.PostAsJsonAsync($"/api/datasets/{created.Id}/import-folder", new ImportFolderRequest(_sourceFolder));
                IngestionStatusDto status = IngestionStatusDto.Pending;
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(200);
                    DatasetDetailDto polled = (await (await _client.GetAsync($"/api/datasets/{created.Id}"))
                        .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
                    status = polled.Status;
                    if (status is IngestionStatusDto.Completed or IngestionStatusDto.Failed)
                    {
                        return status;
                    }
                }
                return status;
            }

            (await ImportAndWaitAsync()).Should().Be(IngestionStatusDto.Completed);
            DatasetDetailDto afterFirst = (await (await _client.GetAsync($"/api/datasets/{created.Id}"))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            afterFirst.TotalItems.Should().Be(1);

            (await ImportAndWaitAsync()).Should().Be(IngestionStatusDto.Completed);
            DatasetDetailDto afterSecond = (await (await _client.GetAsync($"/api/datasets/{created.Id}"))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            // The old bug would report 1 again here (this run's batch size), not 2 (the true
            // cumulative total across both imports).
            afterSecond.TotalItems.Should().Be(2);
        }
    }
}
