using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.5 (audit U-15, U-14, U-26): the create-dataset forms poll the dataset's real status where a
/// decision depends on it, instead of sleeping a fixed 0.5–5 s and guessing, and failures read as
/// sentences that don't point at a hard-coded https://localhost:7221.
/// </summary>
public sealed class IngestionWorkflowTests
{
    private static readonly Guid DatasetId = Guid.NewGuid();

    /// <summary>Polling stops as soon as the status leaves Pending and returns that status.</summary>
    [Fact]
    public async Task WaitForStatus_ReturnsTheFirstSettledStatus()
    {
        Queue<IngestionStatusDto> statuses = new(new[] { IngestionStatusDto.Pending, IngestionStatusDto.Pending, IngestionStatusDto.Failed });
        SequenceHandler handler = new(statuses, error: "STREAMING_UNAVAILABLE: no parquet export");
        IngestionWorkflowService workflow = Create(handler);

        DatasetDetailDto? result = await workflow.WaitForStatusAsync(
            DatasetId, d => d.Status != IngestionStatusDto.Pending, TimeSpan.FromSeconds(10));

        Assert.NotNull(result);
        Assert.Equal(IngestionStatusDto.Failed, result!.Status);
        Assert.Equal(3, handler.Calls);
    }

    /// <summary>When nothing settles before the timeout, the last status read is returned rather than hanging.</summary>
    [Fact]
    public async Task WaitForStatus_GivesUpAtTheTimeout()
    {
        SequenceHandler handler = new(new Queue<IngestionStatusDto>(), error: null, repeat: IngestionStatusDto.Pending);
        IngestionWorkflowService workflow = Create(handler);

        DatasetDetailDto? result = await workflow.WaitForStatusAsync(
            DatasetId, d => d.Status == IngestionStatusDto.Completed, TimeSpan.FromMilliseconds(50));

        Assert.Equal(IngestionStatusDto.Pending, result!.Status);
        Assert.True(handler.Calls >= 1);
    }

    /// <summary>Failures map to plain sentences by cause; none mention a hard-coded origin.</summary>
    [Theory]
    [InlineData(null, "The server didn't respond. Check that the API is running, then retry.")]
    [InlineData(HttpStatusCode.Forbidden, "You don't have permission to do that. Log in again and retry.")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "The file is larger than the server accepts.")]
    [InlineData(HttpStatusCode.Conflict, "The dataset was changed by someone else. Reload it and try again.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The server's import queue is full. Wait a minute, then try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "The server returned an error (HTTP 500). Retry, or check the API logs.")]
    public void DescribeFailure_NamesTheCause(HttpStatusCode? status, string expected)
    {
        string message = IngestionWorkflowService.DescribeFailure(new HttpRequestException("x", null, status));

        Assert.Equal(expected, message);
        Assert.DoesNotContain("7221", message);
    }

    /// <summary>The picker offers exactly the types the server parses (it used to offer .tsv000/.csv000/.txt, which failed after upload).</summary>
    [Fact]
    public void AcceptedExtensions_MatchTheServerParsers()
    {
        Assert.Equal(".csv,.tsv,.json,.jsonl,.ndjson,.zip", IngestionWorkflowService.AcceptedUploadExtensions);
    }

    /// <summary>File sizes read naturally in the upload form.</summary>
    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void FormatSize_IsReadable(long bytes, string expected)
    {
        Assert.Equal(expected, UploadSourceForm.FormatSize(bytes));
    }

    /// <summary>
    /// A new HuggingFace streaming dataset's record says 0 items until the server serves its first
    /// page; the viewer takes the total from that page (found in the PR 2.5 end-to-end check, where
    /// the status band read "0 items" over a 60,000-row dataset).
    /// </summary>
    [Fact]
    public void WithPageTotal_FillsAMissingTotalFromTheFirstPage()
    {
        DatasetDetailDto fresh = new() { Id = DatasetId, Name = "mnist", TotalItems = 0 };
        DatasetStudio.DTO.Common.PageResponse<DatasetItemDto> page = new() { Items = Array.Empty<DatasetItemDto>(), TotalCount = 60_000 };

        Assert.Equal(60_000, DatasetCacheService.WithPageTotal(fresh, page).TotalItems);
        Assert.Equal(5, DatasetCacheService.WithPageTotal(fresh with { TotalItems = 5 }, page).TotalItems);
        Assert.Equal(0, DatasetCacheService.WithPageTotal(fresh, null).TotalItems);
    }

    private static IngestionWorkflowService Create(HttpMessageHandler handler)
    {
        DatasetApiClient api = new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") });
        return new IngestionWorkflowService(api) { PollInterval = TimeSpan.FromMilliseconds(1) };
    }

    /// <summary>Answers GET /api/datasets/{id} with the next queued status (or a fixed one).</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<IngestionStatusDto> _statuses;
        private readonly string? _error;
        private readonly IngestionStatusDto _repeat;

        public SequenceHandler(Queue<IngestionStatusDto> statuses, string? error, IngestionStatusDto repeat = IngestionStatusDto.Pending)
        {
            _statuses = statuses;
            _error = error;
            _repeat = repeat;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            IngestionStatusDto status = _statuses.Count > 0 ? _statuses.Dequeue() : _repeat;
            string json = JsonSerializer.Serialize(new { id = DatasetId, name = "x", status = (int)status, errorMessage = _error });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
