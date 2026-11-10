using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
// Same alias convention as StreamingIngestionTests: DatasetIngestionService uses the Core repository
// interfaces, not the unrelated Services.DatasetManagement item repository.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit H-1(1): a streaming import whose config and split were left unset (the client sends
    /// nulls when discovery found a single option) must resolve the default split server-side,
    /// record its row count, and persist the resolved config/split so later page requests do not
    /// depend on re-discovery. The old code compared null config/split with Ordinal equality, so
    /// no split ever matched and the count was never written.
    /// </summary>
    public sealed class HuggingFaceImportResolutionTests
    {
        private static DatasetIngestionService CreateService(
            IDatasetRepository datasetRepository,
            IHuggingFaceDatasetServerClient datasetServerClient)
        {
            IConfiguration config = new ConfigurationBuilder().Build();
            return new DatasetIngestionService(
                datasetRepository,
                new Mock<IDatasetItemRepository>().Object,
                new Mock<IHuggingFaceClient>().Object,
                datasetServerClient,
                config,
                NullLogger<DatasetIngestionService>.Instance);
        }

        [Fact]
        public async Task ImportStreaming_WithoutConfigOrSplit_ResolvesTrainSplitCountAndPersistsIt()
        {
            Guid datasetId = Guid.NewGuid();
            var datasetRepoMock = new Mock<IDatasetRepository>();
            datasetRepoMock
                .Setup(r => r.GetByIdAsync(datasetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DatasetDto
                {
                    Id = datasetId,
                    Name = "beans",
                    SourceType = DatasetSourceType.HuggingFaceStreaming,
                    IsStreaming = true,
                    HuggingFaceRepository = "AI-Lab-Makerere/beans",
                });

            var datasetServerMock = new Mock<IHuggingFaceDatasetServerClient>();
            datasetServerMock
                .Setup(c => c.GetAllSplitsAsync("AI-Lab-Makerere/beans", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<HuggingFaceDatasetSplitInfo>
                {
                    new() { Dataset = "AI-Lab-Makerere/beans", Config = "default", Split = "test", NumRows = 128 },
                    new() { Dataset = "AI-Lab-Makerere/beans", Config = "default", Split = "train", NumRows = 1_034 },
                });

            await CreateService(datasetRepoMock.Object, datasetServerMock.Object).ImportFromHuggingFaceAsync(
                datasetId,
                new ImportHuggingFaceDatasetRequest { Repository = "AI-Lab-Makerere/beans", IsStreaming = true },
                CancellationToken.None);

            datasetRepoMock.Verify(r => r.UpdateItemCountAsync(datasetId, 1_034, It.IsAny<CancellationToken>()), Times.Once);
            datasetRepoMock.Verify(r => r.UpdateAsync(
                It.IsAny<DatasetDto>(),
                It.Is<DatasetDto>(d => d.Id == datasetId && d.HuggingFaceConfig == "default" && d.HuggingFaceSplit == "train"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ImportStreaming_ExplicitConfigWithoutItsSplit_DoesNotBorrowAnotherConfigsSplit()
        {
            // Codex P2 on PR #7: the match is persisted as the dataset's source, so a split that exists
            // only under another config must not be recorded for an explicitly requested config.
            Guid datasetId = Guid.NewGuid();
            var datasetRepoMock = new Mock<IDatasetRepository>();
            var datasetServerMock = new Mock<IHuggingFaceDatasetServerClient>();
            datasetServerMock
                .Setup(c => c.GetAllSplitsAsync("org/ds", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<HuggingFaceDatasetSplitInfo>
                {
                    new() { Dataset = "org/ds", Config = "B", Split = "validation", NumRows = 50 },
                });

            await CreateService(datasetRepoMock.Object, datasetServerMock.Object).ImportFromHuggingFaceAsync(
                datasetId,
                new ImportHuggingFaceDatasetRequest { Repository = "org/ds", IsStreaming = true, Config = "A", Split = "validation" },
                CancellationToken.None);

            datasetRepoMock.Verify(r => r.UpdateItemCountAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
            datasetRepoMock.Verify(r => r.UpdateAsync(It.IsAny<DatasetDto>(), It.IsAny<DatasetDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
