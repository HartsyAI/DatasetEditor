using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class ParquetItemRepositoryTests
    {
        private static string CreateUniqueDataDirectory()
        {
            string baseRoot = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ParquetItemRepositoryTests");
            Directory.CreateDirectory(baseRoot);
            string folderName = Guid.NewGuid().ToString("N");
            string dataDirectory = Path.Combine(baseRoot, folderName);
            Directory.CreateDirectory(dataDirectory);
            return dataDirectory;
        }

        [Fact]
        public async Task AddRangeAndGetPageAsync_RoundTripsItems()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);

                Guid datasetId = Guid.NewGuid();

                List<DatasetItemDto> items = new List<DatasetItemDto>
                {
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = datasetId,
                        ExternalId = "item-1",
                        Title = "First item",
                        Description = "Description 1",
                        Width = 100,
                        Height = 50,
                        Tags = new List<string> { "tag1" },
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = datasetId,
                        ExternalId = "item-2",
                        Title = "Second item",
                        Description = "Description 2",
                        Width = 200,
                        Height = 100,
                        Tags = new List<string> { "tag2" },
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await repository.AddRangeAsync(datasetId, items, CancellationToken.None);

                (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) page =
                    await repository.GetPageAsync(datasetId, null, null, 10, CancellationToken.None);

                page.Items.Count.Should().Be(2);
                page.NextCursor.Should().BeNull();
                page.Items[0].ExternalId.Should().Be("item-1");
                page.Items[1].ExternalId.Should().Be("item-2");
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }

        [Fact]
        public async Task GetItemAndGetCountAsync_WorkAfterAddRange()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);

                Guid datasetId = Guid.NewGuid();

                DatasetItemDto first = new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = "item-1",
                    Title = "First",
                    Width = 10,
                    Height = 5,
                    Tags = new List<string>(),
                    Metadata = new Dictionary<string, string>(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                DatasetItemDto second = new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = "item-2",
                    Title = "Second",
                    Width = 20,
                    Height = 10,
                    Tags = new List<string>(),
                    Metadata = new Dictionary<string, string>(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                List<DatasetItemDto> items = new List<DatasetItemDto> { first, second };

                await repository.AddRangeAsync(datasetId, items, CancellationToken.None);

                DatasetItemDto? loaded = await repository.GetItemAsync(first.Id, CancellationToken.None);
                loaded.Should().NotBeNull();
                if (loaded != null)
                {
                    loaded.ExternalId.Should().Be("item-1");
                }

                long count = await repository.GetCountAsync(datasetId, null, CancellationToken.None);
                count.Should().Be(2);
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }

        [Fact]
        public async Task DeleteByDatasetAsync_RemovesAllItems()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);

                Guid datasetId = Guid.NewGuid();

                List<DatasetItemDto> items = new List<DatasetItemDto>
                {
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = datasetId,
                        ExternalId = "item-1",
                        Title = "First",
                        Width = 10,
                        Height = 5,
                        Tags = new List<string>(),
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await repository.AddRangeAsync(datasetId, items, CancellationToken.None);

                await repository.DeleteByDatasetAsync(datasetId, CancellationToken.None);

                (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) page =
                    await repository.GetPageAsync(datasetId, null, null, 10, CancellationToken.None);

                page.Items.Count.Should().Be(0);
                long count = await repository.GetCountAsync(datasetId, null, CancellationToken.None);
                count.Should().Be(0);
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }
    }
}
