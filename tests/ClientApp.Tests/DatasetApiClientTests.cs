using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp
{
    public sealed class DatasetApiClientTests
    {
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = _handler(request);
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task GetAllDatasetsAsync_ReturnsDeserializedSummaries()
        {
            string datasetIdString = "11111111-2222-3333-4444-555555555555";
            Guid datasetId = Guid.Parse(datasetIdString);

            string json = "{""datasets"":[{""id"":""" + datasetIdString + """,""name"":""Test dataset"",""description"":""Phase 2 validation"",""status"":0,""totalItems"":5,""createdAt"":""2025-01-01T00:00:00Z"",""updatedAt"":""2025-01-01T00:00:00Z""}],""totalCount"":1,""page"":0,""pageSize"":50}";

            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(request =>
            {
                Assert.Equal("api/datasets?page=0&pageSize=50", request.RequestUri != null ? request.RequestUri.ToString() : string.Empty);

                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                return response;
            });

            HttpClient httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost/")
            };

            DatasetApiClient apiClient = new DatasetApiClient(httpClient);

            IReadOnlyList<DatasetSummaryDto> datasets = await apiClient.GetAllDatasetsAsync(0, 50, CancellationToken.None);

            Assert.NotNull(datasets);
            Assert.Single(datasets);

            DatasetSummaryDto summary = datasets[0];
            Assert.Equal(datasetId, summary.Id);
            Assert.Equal("Test dataset", summary.Name);
            Assert.Equal("Phase 2 validation", summary.Description);
            Assert.Equal(5, summary.TotalItems);
        }

        [Fact]
        public async Task GetAllDatasetsAsync_HandlesMissingDatasetsProperty()
        {
            string json = "{""totalCount"":0,""page"":0,""pageSize"":50}";

            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(request =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                return response;
            });

            HttpClient httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost/")
            };

            DatasetApiClient apiClient = new DatasetApiClient(httpClient);

            IReadOnlyList<DatasetSummaryDto> datasets = await apiClient.GetAllDatasetsAsync(0, 50, CancellationToken.None);

            Assert.NotNull(datasets);
            Assert.Empty(datasets);
        }
    }
}
