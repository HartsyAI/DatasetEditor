using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
// Same convention as the other endpoint-level tests: the Core repository interface is referenced by
// alias, because the API layer has its own, unrelated IDatasetItemRepository in the same namespace.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Upload staging: the file is staged under Storage:UploadPath, the configured volume, not
    /// Path.GetTempPath() (audit I-5), and a full ingestion queue refuses the upload with 503
    /// before anything is staged (audit I-4). Calls the handler directly with a recording queue
    /// so the queued job can be inspected without a worker running it.
    /// </summary>
    public sealed class UploadStagingTests : IDisposable
    {
        private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "upload-staging", Guid.NewGuid().ToString("N"));

        /// <summary>Records reservations, commits and releases, and refuses when the test says the queue is full.</summary>
        private sealed class RecordingQueue : IIngestionJobQueue
        {
            private readonly Dictionary<Guid, IngestionJob> _reserved = new();

            public bool HasCapacity { get; init; } = true;

            /// <summary>Jobs committed to the queue.</summary>
            public List<IngestionJob> Jobs { get; } = new();

            public List<Guid> Released { get; } = new();

            public Task<Guid?> TryReserveAsync(IngestionJob job, CancellationToken cancellationToken)
            {
                if (!HasCapacity)
                {
                    return Task.FromResult<Guid?>(null);
                }
                Guid id = Guid.NewGuid();
                _reserved[id] = job;
                return Task.FromResult<Guid?>(id);
            }

            public Task CommitAsync(Guid jobId, CancellationToken cancellationToken)
            {
                Jobs.Add(_reserved[jobId]);
                return Task.CompletedTask;
            }

            public Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken)
            {
                Released.Add(jobId);
                return Task.CompletedTask;
            }

            public Task<bool> TryEnqueueAsync(IngestionJob job, CancellationToken cancellationToken)
            {
                if (HasCapacity)
                {
                    Jobs.Add(job);
                }
                return Task.FromResult(HasCapacity);
            }

            public Task DiscardQueuedJobsAsync(Guid datasetId, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public void Dispose()
        {
            if (Directory.Exists(_uploadRoot))
            {
                Directory.Delete(_uploadRoot, recursive: true);
            }
        }

        private async Task<(IResult Result, HttpContext Context)> UploadAsync(
            Guid datasetId, RecordingQueue queue, string? uploadPath = null, bool datasetUpdateFails = false)
        {
            DatasetDto dataset = new() { Id = datasetId, Name = "staged" };

            var datasetRepository = new Mock<IDatasetRepository>();
            datasetRepository
                .Setup(r => r.GetByIdAsync(datasetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataset);
            if (datasetUpdateFails)
            {
                // What happens when the dataset is deleted while its upload is being written.
                datasetRepository
                    .Setup(r => r.UpdateAsync(It.IsAny<DatasetDto>(), It.IsAny<DatasetDto>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Dataset not found"));
            }

            var permissionService = new Mock<IDatasetPermissionService>();
            permissionService
                .Setup(p => p.CanReadAsync(It.IsAny<DatasetDto>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            permissionService
                .Setup(p => p.CanWriteAsync(It.IsAny<DatasetDto>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadPath"] = uploadPath ?? _uploadRoot })
                .Build();

            byte[] content = Encoding.UTF8.GetBytes("id,title\n1,a\n");
            IFormFile file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "rows.csv");
            DefaultHttpContext httpContext = new();

            IResult result = await DatasetEndpoints.UploadDatasetFile(
                datasetId,
                file,
                datasetRepository.Object,
                queue,
                configuration,
                permissionService.Object,
                httpContext,
                CancellationToken.None);
            return (result, httpContext);
        }

        [Fact]
        public async Task UploadDatasetFile_StagesFileUnderConfiguredUploadPath()
        {
            RecordingQueue queue = new();

            await UploadAsync(Guid.NewGuid(), queue);

            queue.Jobs.Should().ContainSingle("the upload must enqueue an ingestion job");
            IngestionJob job = queue.Jobs[0];
            job.FilePath.Should().NotBeNullOrEmpty();
            job.FilePath!.Should().StartWith(_uploadRoot + Path.DirectorySeparatorChar);
            File.Exists(job.FilePath).Should().BeTrue("the staged file must exist where the job says it is");
        }

        /// <summary>The job row outlives the process, so the staged path it stores must not depend on the working directory.</summary>
        [Fact]
        public async Task UploadDatasetFile_RelativeUploadPath_QueuesAnAbsolutePath()
        {
            RecordingQueue queue = new();
            string relativeRoot = Path.GetRelativePath(Directory.GetCurrentDirectory(), _uploadRoot);

            await UploadAsync(Guid.NewGuid(), queue, relativeRoot);

            queue.Jobs.Should().ContainSingle();
            Path.IsPathRooted(queue.Jobs[0].FilePath).Should().BeTrue();
            queue.Jobs[0].FilePath!.Should().StartWith(_uploadRoot + Path.DirectorySeparatorChar);
        }

        /// <summary>A reservation whose upload can't be finished is released and its file deleted.</summary>
        [Fact]
        public async Task UploadDatasetFile_StagingFails_ReleasesTheSlotAndDeletesTheFile()
        {
            RecordingQueue queue = new();

            Func<Task> act = () => UploadAsync(Guid.NewGuid(), queue, datasetUpdateFails: true);

            await act.Should().ThrowAsync<InvalidOperationException>();
            queue.Jobs.Should().BeEmpty();
            queue.Released.Should().ContainSingle();
            Directory.EnumerateFiles(_uploadRoot).Should().BeEmpty("the staged file must not outlive its reservation");
        }

        [Fact]
        public async Task UploadDatasetFile_QueueFull_Returns503WithRetryAfter_AndStagesNothing()
        {
            RecordingQueue queue = new() { HasCapacity = false };

            (IResult result, HttpContext context) = await UploadAsync(Guid.NewGuid(), queue);

            context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
            context.Response.Body = new MemoryStream();
            await result.ExecuteAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            context.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
            queue.Jobs.Should().BeEmpty();
            (Directory.Exists(_uploadRoot) && Directory.EnumerateFiles(_uploadRoot).Any())
                .Should().BeFalse("a refused upload must not leave a staged file behind");
        }
    }
}
