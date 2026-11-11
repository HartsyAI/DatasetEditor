using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 6.4 (audit Q-3): every text format the upload page accepts, through the real multipart
    /// route, the queue and the background worker, read back through the items endpoint; and
    /// deleting the dataset removes its stored files. These paths were only covered piecewise
    /// (the parser with mocks, the route with a stub queue), never end to end.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class UploadFormatsEndToEndTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;

        public UploadFormatsEndToEndTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();
            (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("formats-user", "formats-user@example.com", "password123")))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
            await _factory.DropDatabaseAsync();
        }

        private async Task<DatasetDto> UploadAndWaitAsync(string fileName, string content)
        {
            DatasetDto dataset = (await (await _client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("formats-" + fileName, null)))
                .Content.ReadFromJsonAsync<DatasetDto>())!;

            using MultipartFormDataContent form = new();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);
            HttpResponseMessage upload = await _client.PostAsync($"/api/datasets/{dataset.Id}/upload", form);
            upload.StatusCode.Should().Be(HttpStatusCode.Accepted, await upload.Content.ReadAsStringAsync());

            for (int i = 0; i < 300; i++)
            {
                dataset = (await _client.GetFromJsonAsync<DatasetDto>($"/api/datasets/{dataset.Id}"))!;
                if (dataset.Status is IngestionStatusDto.Completed or IngestionStatusDto.Failed)
                {
                    break;
                }
                await Task.Delay(100);
            }
            dataset.Status.Should().Be(IngestionStatusDto.Completed, dataset.ErrorMessage);
            return dataset;
        }

        [Theory]
        [InlineData("items.csv", "title,image_url,width\nfirst,https://example.com/1.jpg,640\nsecond,https://example.com/2.jpg,800\n")]
        [InlineData("items.tsv", "title\timage_url\twidth\nfirst\thttps://example.com/1.jpg\t640\nsecond\thttps://example.com/2.jpg\t800\n")]
        [InlineData("items.json", "[{\"title\":\"first\",\"image_url\":\"https://example.com/1.jpg\",\"width\":640},{\"title\":\"second\",\"image_url\":\"https://example.com/2.jpg\",\"width\":800}]")]
        [InlineData("items.jsonl", "{\"title\":\"first\",\"image_url\":\"https://example.com/1.jpg\",\"width\":640}\n{\"title\":\"second\",\"image_url\":\"https://example.com/2.jpg\",\"width\":800}\n")]
        public async Task Upload_EachTextFormat_IsIngestedAndServed(string fileName, string content)
        {
            DatasetDto dataset = await UploadAndWaitAsync(fileName, content);

            dataset.TotalItems.Should().Be(2);
            PageResponse<DatasetItemDto>? page = await _client.GetFromJsonAsync<PageResponse<DatasetItemDto>>($"/api/datasets/{dataset.Id}/items?pageSize=10");
            page!.Items.Select(i => i.Title).Should().Equal("first", "second");
            page.Items.Select(i => i.ImageUrl).Should().Equal("https://example.com/1.jpg", "https://example.com/2.jpg");
            page.Items[1].Width.Should().Be(800);
            page.Items.Should().OnlyContain(i => i.DatasetId == dataset.Id);
        }

        [Fact]
        public async Task DeleteDataset_RemovesItsStoredItemFiles()
        {
            DatasetDto dataset = await UploadAndWaitAsync("to-delete.csv", "title,image_url\nonly,https://example.com/a.jpg\n");
            string idPart = dataset.Id.ToString("N");
            string[] StoredFiles() => Directory.Exists(_factory.ParquetPath)
                ? Directory.GetFiles(_factory.ParquetPath, "*", SearchOption.AllDirectories).Where(f => f.Contains(idPart, StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<string>();
            StoredFiles().Should().NotBeEmpty("ingestion writes the dataset's items to Parquet");

            (await _client.DeleteAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            StoredFiles().Should().BeEmpty("deleting a dataset deletes its item files");
            (await _client.GetAsync($"/api/datasets/{dataset.Id}/items")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
