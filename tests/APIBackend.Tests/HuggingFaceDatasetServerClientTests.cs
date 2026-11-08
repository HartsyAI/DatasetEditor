using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Integration;
using FluentAssertions;
using FluentAssertions.Specialized;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Covers the datasets-server client's contract for audit H-6 (upstream failures surface as
    /// <see cref="HuggingFaceUpstreamException"/>, not as null) and H-1(4) (the row count for a
    /// config/split is that split's count, not the whole dataset's total). Each case fails on the
    /// code it replaced: the old client returned null on failure and returned the dataset total.
    /// </summary>
    public sealed class HuggingFaceDatasetServerClientTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_respond(request));
            }
        }

        private static HuggingFaceDatasetServerClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            new(new HttpClient(new StubHandler(respond)), NullLogger<HuggingFaceDatasetServerClient>.Instance);

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        private const string SizeBody = """
            {"size":{"dataset":{"num_rows":70000},"splits":[
              {"dataset":"org/ds","config":"default","split":"train","num_rows":60000},
              {"dataset":"org/ds","config":"default","split":"test","num_rows":10000}
            ]}}
            """;

        [Fact]
        public async Task GetRowsAsync_UpstreamNotFound_ThrowsUpstreamExceptionWithStatus()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            Func<Task> act = () => client.GetRowsAsync("org/ds", null, "train", 0, 100, null);

            ExceptionAssertions<HuggingFaceUpstreamException> ex = await act.Should().ThrowAsync<HuggingFaceUpstreamException>();
            ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetRowsAsync_TransportFailure_ThrowsUpstreamException()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => throw new HttpRequestException("connection reset"));

            Func<Task> act = () => client.GetRowsAsync("org/ds", null, "train", 0, 100, null);

            await act.Should().ThrowAsync<HuggingFaceUpstreamException>();
        }

        [Fact]
        public async Task GetRowsAsync_UnparseableBody_ThrowsUpstreamException()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => Json("not json at all"));

            Func<Task> act = () => client.GetRowsAsync("org/ds", null, "train", 0, 100, null);

            await act.Should().ThrowAsync<HuggingFaceUpstreamException>();
        }

        [Fact]
        public async Task GetDatasetSizeAsync_RequestedSplit_ReturnsThatSplitsCountNotDatasetTotal()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => Json(SizeBody));

            HuggingFaceDatasetSizeInfo? info = await client.GetDatasetSizeAsync("org/ds", "default", "test", null);

            info.Should().NotBeNull();
            info!.NumRows.Should().Be(10_000, "the count must be the selected split's rows, not the sum over every split");
        }

        [Fact]
        public async Task GetDatasetSizeAsync_NoSplitRequested_ReturnsTrainSplitCount()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => Json(SizeBody));

            HuggingFaceDatasetSizeInfo? info = await client.GetDatasetSizeAsync("org/ds", null, null, null);

            info.Should().NotBeNull();
            info!.Config.Should().Be("default");
            info.Split.Should().Be("train");
            info.NumRows.Should().Be(60_000);
        }

        [Fact]
        public async Task GetDatasetSizeAsync_UpstreamForbidden_ThrowsUpstreamExceptionWithStatus()
        {
            HuggingFaceDatasetServerClient client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

            Func<Task> act = () => client.GetDatasetSizeAsync("org/ds", null, null, null);

            ExceptionAssertions<HuggingFaceUpstreamException> ex = await act.Should().ThrowAsync<HuggingFaceUpstreamException>();
            ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
