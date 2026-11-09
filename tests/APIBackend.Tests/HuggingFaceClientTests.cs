using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.Integration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 3.2 (audit I-7, H-4): the Hub file tree is listed recursively across pages, gated
    /// repositories are recognised, and downloads stop at the size limit and leave no partial file.
    /// </summary>
    public sealed class HuggingFaceClientTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "hf-client", Guid.NewGuid().ToString("N"));

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public List<string> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
                return Task.FromResult(_respond(request));
            }
        }

        /// <summary>A body whose length the response can't report, like a chunked transfer.</summary>
        private sealed class UnknownLengthStream : MemoryStream
        {
            public UnknownLengthStream(byte[] buffer) : base(buffer) { }

            public override bool CanSeek => false;
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task GetDatasetInfo_ListsTheTreeRecursivelyAcrossPages_AndRecognisesGatedRepos()
        {
            const string nextPage = "https://huggingface.co/api/datasets/org/ds/tree/main?recursive=true&cursor=page2";
            StubHandler handler = new(request =>
            {
                string url = request.RequestUri!.ToString();
                if (url == "https://huggingface.co/api/datasets/org/ds")
                {
                    return Json("""{"_id":"x","author":"org","gated":"auto","private":false}""");
                }
                if (url == nextPage)
                {
                    return Json("""[{"type":"file","path":"data/train-00000.parquet","size":100}]""");
                }
                HttpResponseMessage first = Json("""[{"type":"directory","path":"data"},{"type":"file","path":"README.md","size":10}]""");
                first.Headers.TryAddWithoutValidation("Link", $"<{nextPage}>; rel=\"next\"");
                return first;
            });
            HuggingFaceClient client = new(new HttpClient(handler), NullLogger<HuggingFaceClient>.Instance);

            HuggingFaceDatasetInfo? info = await client.GetDatasetInfoAsync("org/ds");

            info.Should().NotBeNull("a gated repository's \"gated\": \"auto\" must not break parsing");
            info!.Gated.Should().BeTrue();
            info.Files.Select(f => f.Path).Should().Equal("README.md", "data/train-00000.parquet");
            handler.Requests.Should().Contain("https://huggingface.co/api/datasets/org/ds/tree/main?recursive=true");
        }

        [Fact]
        public async Task DownloadFile_ContentLengthOverLimit_ThrowsAndWritesNothing()
        {
            HuggingFaceClient client = new(
                new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1_000]) })),
                NullLogger<HuggingFaceClient>.Instance);
            string destination = Path.Combine(_folder, "big.parquet");

            Func<Task> act = () => client.DownloadFileAsync("org/ds", "data/big.parquet", destination, maxBytes: 100);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*download limit*");
            File.Exists(destination).Should().BeFalse();
        }

        [Fact]
        public async Task DownloadFile_UnknownLengthOverLimit_StopsAndDeletesThePartialFile()
        {
            HuggingFaceClient client = new(
                new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new UnknownLengthStream(new byte[300_000]))
                })),
                NullLogger<HuggingFaceClient>.Instance);
            string destination = Path.Combine(_folder, "big.parquet");

            Func<Task> act = () => client.DownloadFileAsync("org/ds", "data/big.parquet", destination, maxBytes: 100_000);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*download limit*");
            File.Exists(destination).Should().BeFalse("a partial download must not be left behind");
        }

        [Fact]
        public async Task DownloadFile_EscapesPathSegmentsButKeepsFolders()
        {
            StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
            HuggingFaceClient client = new(new HttpClient(handler), NullLogger<HuggingFaceClient>.Instance);

            await client.DownloadFileAsync("org/ds", "data/my file#1.csv", Path.Combine(_folder, "f.csv"));

            handler.Requests.Should().ContainSingle()
                .Which.Should().Be("https://huggingface.co/datasets/org/ds/resolve/main/data/my%20file%231.csv");
        }
    }
}
