using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit I-3: a long import reports progress. The dataset's item count is updated after every
    /// chunk, so the client's status poll shows rows arriving instead of 0 until the very end.
    /// </summary>
    public sealed class IngestionProgressTests
    {
        [Fact]
        public async Task StartIngestion_UpdatesItemCountAfterEachChunk()
        {
            string folder = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ingestion-progress", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string csvPath = Path.Combine(folder, "rows.csv");
            StringBuilder csv = new("title,image_url\n");
            for (int i = 0; i < 22_500; i++)
            {
                csv.Append($"item-{i},https://example.com/{i}.jpg\n");
            }
            await File.WriteAllTextAsync(csvPath, csv.ToString());

            try
            {
                Guid datasetId = Guid.NewGuid();
                List<long> reportedCounts = new();
                var datasets = new Mock<IDatasetRepository>();
                datasets
                    .Setup(r => r.UpdateItemCountAsync(datasetId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .Callback((Guid _, long count, CancellationToken _) => reportedCounts.Add(count))
                    .Returns(Task.CompletedTask);

                // 100 items already in the dataset (a second upload appends), 22,600 at the end.
                var items = new Mock<IDatasetItemRepository>();
                items.SetupSequence(r => r.GetCountAsync(datasetId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(100)
                    .ReturnsAsync(22_600);

                DatasetIngestionService service = new(
                    datasets.Object,
                    items.Object,
                    new Mock<IHuggingFaceClient>().Object,
                    new Mock<IHuggingFaceDatasetServerClient>().Object,
                    new ConfigurationBuilder().Build(),
                    NullLogger<DatasetIngestionService>.Instance);

                await service.StartIngestionAsync(datasetId, csvPath, CancellationToken.None);

                reportedCounts.Should().Equal(new long[] { 10_100, 20_100, 22_600 },
                    "each full chunk reports the running total, then the final count is the true total");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
