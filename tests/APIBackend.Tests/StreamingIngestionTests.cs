using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
// DatasetIngestionService depends on the Core repository interfaces specifically -- there's a
// second, unrelated IDatasetItemRepository in Services.DatasetManagement (the ParquetItemRepository-
// backed one used by ItemEditEndpoints), which the DatasetManagement using above would otherwise
// collide with.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;
using IDatasetItemRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetItemRepository;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Phase 2: proves ingestion is genuinely streaming/chunked, not just re-chunked after a
    /// full in-memory parse. A "max in-flight buffer never exceeds chunk size" test would pass
    /// even if a parser still buffered the whole file internally (the chunker's own buffer
    /// bounds itself by construction) -- these tests instead assert the two properties that
    /// actually distinguish streaming from that: (1) a parser yields items before its source
    /// stream is fully consumed, and (2) ingestion writes in multiple bounded batches instead
    /// of one batch sized to the whole file.
    /// </summary>
    public sealed class StreamingIngestionTests
    {
        private static DatasetIngestionService CreateService(
            IDatasetRepository datasetRepository,
            IDatasetItemRepository itemRepository,
            IHuggingFaceDatasetServerClient? datasetServerClient = null)
        {
            var hfClient = new Mock<IHuggingFaceClient>().Object;
            datasetServerClient ??= new Mock<IHuggingFaceDatasetServerClient>().Object;
            IConfiguration config = new ConfigurationBuilder().Build();
            return new DatasetIngestionService(datasetRepository, itemRepository, hfClient, datasetServerClient, config, NullLogger<DatasetIngestionService>.Instance);
        }

        private static string BuildCsv(int rowCount)
        {
            var sb = new StringBuilder();
            sb.Append("title,image_url\n");
            for (int i = 0; i < rowCount; i++)
            {
                sb.Append($"item-{i},https://example.com/{i}.jpg\n");
            }
            return sb.ToString();
        }

        [Fact]
        public async Task ParseCsvAsync_YieldsItemsBeforeConsumingTheWholeStream()
        {
            // 5,000 rows -- well past one 2,000-item chunk -- so "yielded the first chunk's
            // worth of rows" and "consumed the whole stream" are clearly distinguishable.
            byte[] csvBytes = Encoding.UTF8.GetBytes(BuildCsv(5_000));
            using var stream = new MemoryStream(csvBytes);

            var service = CreateService(new Mock<IDatasetRepository>().Object, new Mock<IDatasetItemRepository>().Object);

            int yielded = 0;
            long streamPositionAtChunkBoundary = -1;
            await foreach (DatasetItemDto item in service.ParseCsvAsync(Guid.NewGuid(), stream, CancellationToken.None))
            {
                yielded++;
                if (yielded == 2_000)
                {
                    streamPositionAtChunkBoundary = stream.Position;
                    break;
                }
            }

            yielded.Should().Be(2_000);
            streamPositionAtChunkBoundary.Should().BeGreaterThan(0);
            streamPositionAtChunkBoundary.Should().BeLessThan(csvBytes.Length,
                "a true streaming parser should not have read the entire file just to produce its first 2,000 items");
        }

        [Fact]
        public async Task StartIngestionAsync_LargeCsv_WritesInMultipleBoundedChunksNotOneGiantInsert()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"streaming-ingestion-test-{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(tempFile, BuildCsv(25_000));

            try
            {
                var datasetRepoMock = new Mock<IDatasetRepository>();
                var itemRepoMock = new Mock<IDatasetItemRepository>();
                var insertedBatchSizes = new List<int>();

                itemRepoMock
                    .Setup(r => r.InsertItemsAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<DatasetItemDto>>(), It.IsAny<CancellationToken>()))
                    .Callback<Guid, IEnumerable<DatasetItemDto>, CancellationToken>((_, items, _) => insertedBatchSizes.Add(items.Count()))
                    .Returns(Task.CompletedTask);
                itemRepoMock
                    .Setup(r => r.GetCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => insertedBatchSizes.Sum());

                var service = CreateService(datasetRepoMock.Object, itemRepoMock.Object);

                await service.StartIngestionAsync(Guid.NewGuid(), tempFile, CancellationToken.None);

                // Old (pre-Phase-2) behavior: exactly 1 call sized to the whole file. The
                // chunked rewrite must write in ceil(25000 / 10000) = 3 bounded batches instead (one
                // row group each, ParquetSchemaDefinition.DefaultBatchSize rows; audit I-9).
                insertedBatchSizes.Should().Equal(10_000, 10_000, 5_000);
                datasetRepoMock.Verify(r => r.UpdateStatusAsync(
                    It.IsAny<Guid>(), IngestionStatusDto.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task StartIngestionAsync_EmptyCsv_FailsInsteadOfSilentlyCompleting()
        {
            // Header row only, no data rows -- the empty-input guard used to be a synchronous
            // items.Count == 0 check; after chunking it has to be re-derived from the drain
            // loop's running total, so a regression here would let an empty file report success.
            string tempFile = Path.Combine(Path.GetTempPath(), $"streaming-ingestion-empty-{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(tempFile, "title,image_url\n");

            try
            {
                var datasetRepoMock = new Mock<IDatasetRepository>();
                var itemRepoMock = new Mock<IDatasetItemRepository>();
                var service = CreateService(datasetRepoMock.Object, itemRepoMock.Object);

                Func<Task> act = () => service.StartIngestionAsync(Guid.NewGuid(), tempFile, CancellationToken.None);

                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("No items were parsed from the file");
                itemRepoMock.Verify(r => r.InsertItemsAsync(
                    It.IsAny<Guid>(), It.IsAny<IEnumerable<DatasetItemDto>>(), It.IsAny<CancellationToken>()), Times.Never);
                datasetRepoMock.Verify(r => r.UpdateStatusAsync(
                    It.IsAny<Guid>(), IngestionStatusDto.Failed, "No items were parsed from the file", It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ImportFromHuggingFaceAsync_Streaming_ResolvesRowCountFromDatasetServer()
        {
            var datasetRepoMock = new Mock<IDatasetRepository>();
            var datasetServerMock = new Mock<IHuggingFaceDatasetServerClient>();
            datasetServerMock
                .Setup(c => c.GetAllSplitsAsync("AI-Lab-Makerere/beans", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<HuggingFaceDatasetSplitInfo>
                {
                    new() { Dataset = "AI-Lab-Makerere/beans", Config = "default", Split = "train", NumRows = 1_034 },
                    new() { Dataset = "AI-Lab-Makerere/beans", Config = "default", Split = "test", NumRows = 128 },
                });

            var service = CreateService(datasetRepoMock.Object, new Mock<IDatasetItemRepository>().Object, datasetServerMock.Object);
            var datasetId = Guid.NewGuid();
            var request = new ImportHuggingFaceDatasetRequest
            {
                Repository = "AI-Lab-Makerere/beans",
                IsStreaming = true,
                Config = "default",
                Split = "train",
            };

            await service.ImportFromHuggingFaceAsync(datasetId, request, CancellationToken.None);

            datasetRepoMock.Verify(r => r.UpdateItemCountAsync(datasetId, 1_034, It.IsAny<CancellationToken>()), Times.Once);
            datasetRepoMock.Verify(r => r.UpdateStatusAsync(
                datasetId, IngestionStatusDto.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ImportFromHuggingFaceAsync_Streaming_RowCountLookupFailure_StillCompletes()
        {
            // A renamed/gated repo or a datasets-server outage must not turn an otherwise
            // successful streaming import into a Failed one -- the count is a display nicety,
            // not a correctness requirement for streaming (rows are still fetched on demand).
            var datasetRepoMock = new Mock<IDatasetRepository>();
            var datasetServerMock = new Mock<IHuggingFaceDatasetServerClient>();
            datasetServerMock
                .Setup(c => c.GetAllSplitsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("dataset has been renamed"));

            var service = CreateService(datasetRepoMock.Object, new Mock<IDatasetItemRepository>().Object, datasetServerMock.Object);
            var datasetId = Guid.NewGuid();
            var request = new ImportHuggingFaceDatasetRequest
            {
                Repository = "some/renamed-dataset",
                IsStreaming = true,
                Config = "default",
                Split = "train",
            };

            await service.ImportFromHuggingFaceAsync(datasetId, request, CancellationToken.None);

            datasetRepoMock.Verify(r => r.UpdateItemCountAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
            datasetRepoMock.Verify(r => r.UpdateStatusAsync(
                datasetId, IngestionStatusDto.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
