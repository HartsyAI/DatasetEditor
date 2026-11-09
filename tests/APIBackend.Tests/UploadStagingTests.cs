using System;
using System.Collections.Generic;
using System.IO;
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
using Moq;
using Xunit;
// Same convention as the other endpoint-level tests: the Core repository interface is referenced by
// alias, because the API layer has its own, unrelated IDatasetItemRepository in the same namespace.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit I-5: an uploaded file is staged under Storage:UploadPath, the configured volume, not
    /// Path.GetTempPath(). Calls the handler directly so the queued job's path can be inspected
    /// without racing the background worker that deletes it.
    /// </summary>
    public sealed class UploadStagingTests
    {
        [Fact]
        public async Task UploadDatasetFile_StagesFileUnderConfiguredUploadPath()
        {
            string uploadRoot = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "upload-staging", Guid.NewGuid().ToString("N"));
            try
            {
                Guid datasetId = Guid.NewGuid();
                DatasetDto dataset = new() { Id = datasetId, Name = "staged" };

                var datasetRepository = new Mock<IDatasetRepository>();
                datasetRepository
                    .Setup(r => r.GetByIdAsync(datasetId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(dataset);

                var permissionService = new Mock<IDatasetPermissionService>();
                permissionService
                    .Setup(p => p.CanReadAsync(It.IsAny<DatasetDto>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                permissionService
                    .Setup(p => p.CanWriteAsync(It.IsAny<DatasetDto>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);

                IConfiguration configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadPath"] = uploadRoot })
                    .Build();

                byte[] content = Encoding.UTF8.GetBytes("id,title\n1,a\n");
                IFormFile file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "rows.csv");
                var queue = new IngestionQueue();

                await DatasetEndpoints.UploadDatasetFile(
                    datasetId,
                    file,
                    datasetRepository.Object,
                    queue,
                    configuration,
                    permissionService.Object,
                    new DefaultHttpContext(),
                    CancellationToken.None);

                queue.Reader.TryRead(out IngestionJob? job).Should().BeTrue("the upload must enqueue an ingestion job");
                job!.FilePath.Should().NotBeNullOrEmpty();
                job.FilePath!.Should().StartWith(uploadRoot + Path.DirectorySeparatorChar);
                File.Exists(job.FilePath).Should().BeTrue("the staged file must exist where the job says it is");
            }
            finally
            {
                if (Directory.Exists(uploadRoot))
                {
                    Directory.Delete(uploadRoot, recursive: true);
                }
            }
        }
    }
}
