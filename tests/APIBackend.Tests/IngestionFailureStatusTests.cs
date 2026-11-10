using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
// Same aliasing as the other ingestion tests: the service uses the Core repository interfaces.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit I-2: when ingestion is cancelled mid-run, the write that records the Failed status must
    /// not reuse the cancelled token. The repository honours cancellation (as EF Core does), so with
    /// the token reused the Failed write throws and the dataset is left in Processing.
    /// </summary>
    public sealed class IngestionFailureStatusTests
    {
        /// <summary>
        /// Builds a repository mock that behaves like the real one: a cancelled token makes the write
        /// throw. Cancels <paramref name="cts"/> on the first Processing write, so the failure happens
        /// after the caller's token has been cancelled. Records every status that actually lands.
        /// </summary>
        private static Mock<IDatasetRepository> CreateCancellingRepository(CancellationTokenSource cts, List<IngestionStatusDto> landed)
        {
            var repository = new Mock<IDatasetRepository>();
            repository
                .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<IngestionStatusDto>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns((Guid _, IngestionStatusDto status, string? _, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    landed.Add(status);
                    if (status == IngestionStatusDto.Processing)
                    {
                        cts.Cancel();
                    }
                    return Task.CompletedTask;
                });
            return repository;
        }

        private static DatasetIngestionService CreateService(IDatasetRepository datasetRepository, IHuggingFaceClient huggingFaceClient)
        {
            IConfiguration config = new ConfigurationBuilder().Build();
            return new DatasetIngestionService(
                datasetRepository,
                new Mock<IDatasetItemRepository>().Object,
                huggingFaceClient,
                new Mock<IHuggingFaceDatasetServerClient>().Object,
                config,
                NullLogger<DatasetIngestionService>.Instance);
        }

        [Fact]
        public async Task StartIngestion_CancelledMidRun_StillRecordsFailedStatus()
        {
            string csvPath = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ingestion-failure", Guid.NewGuid().ToString("N"), "rows.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
            await File.WriteAllTextAsync(csvPath, "id,title\n1,a\n");

            try
            {
                using var cts = new CancellationTokenSource();
                var landed = new List<IngestionStatusDto>();
                Mock<IDatasetRepository> repository = CreateCancellingRepository(cts, landed);
                DatasetIngestionService service = CreateService(repository.Object, new Mock<IHuggingFaceClient>().Object);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => service.StartIngestionAsync(Guid.NewGuid(), csvPath, cts.Token));

                landed.Should().Contain(IngestionStatusDto.Failed,
                    "the failure must be recorded even though the caller's token was cancelled");
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(csvPath)!, recursive: true);
            }
        }

        [Fact]
        public async Task ImportFromHuggingFace_CancelledMidRun_StillRecordsFailedStatus()
        {
            using var cts = new CancellationTokenSource();
            var landed = new List<IngestionStatusDto>();
            Mock<IDatasetRepository> repository = CreateCancellingRepository(cts, landed);
            // Download mode with no dataset info: the import fails after the caller's token was cancelled.
            DatasetIngestionService service = CreateService(repository.Object, new Mock<IHuggingFaceClient>().Object);

            await Assert.ThrowsAnyAsync<Exception>(() => service.ImportFromHuggingFaceAsync(
                Guid.NewGuid(),
                new ImportHuggingFaceDatasetRequest { Repository = "org/ds", IsStreaming = false },
                cts.Token));

            landed.Should().Contain(IngestionStatusDto.Failed,
                "the failure must be recorded even though the caller's token was cancelled");
        }
    }
}
