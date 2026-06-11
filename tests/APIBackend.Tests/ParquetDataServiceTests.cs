using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Storage;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class ParquetDataServiceTests
    {
        private static string CreateUniqueTempFilePath()
        {
            string baseDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ParquetDataServiceTests");
            Directory.CreateDirectory(baseDirectory);
            string fileName = Guid.NewGuid().ToString("N") + ".parquet";
            string filePath = Path.Combine(baseDirectory, fileName);
            return filePath;
        }

        [Fact]
        public async Task WriteAndReadAsync_RoundTripsItems()
        {
            string filePath = CreateUniqueTempFilePath();

            try
            {
                ParquetDataService service = new ParquetDataService();

                List<DatasetItemDto> items = new List<DatasetItemDto>
                {
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = Guid.NewGuid(),
                        ExternalId = "item-1",
                        Title = "Test item 1",
                        Description = "Description",
                        ThumbnailUrl = "thumb",
                        ImageUrl = "image",
                        Width = 640,
                        Height = 480,
                        Tags = new List<string> { "tag1", "tag2" },
                        IsFavorite = true,
                        Metadata = new Dictionary<string, string> { { "k", "v" } },
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await service.WriteAsync(filePath, items, CancellationToken.None);
                PagedResult<DatasetItemDto> result = await service.ReadAsync(filePath, 0, 10, CancellationToken.None);

                result.TotalCount.Should().Be(1);
                result.Items.Count.Should().Be(1);
                DatasetItemDto item = result.Items[0];
                item.ExternalId.Should().Be("item-1");
                item.Tags.Should().Contain("tag1");
                item.Metadata["k"].Should().Be("v");
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        [Fact]
        public async Task GetCountAsync_ReturnsTotalItemCount()
        {
            string filePath = CreateUniqueTempFilePath();

            try
            {
                ParquetDataService service = new ParquetDataService();

                List<DatasetItemDto> items = new List<DatasetItemDto>
                {
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = Guid.NewGuid(),
                        ExternalId = "item-1",
                        Title = "First",
                        Width = 1,
                        Height = 1,
                        Tags = new List<string>(),
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = Guid.NewGuid(),
                        ExternalId = "item-2",
                        Title = "Second",
                        Width = 1,
                        Height = 1,
                        Tags = new List<string>(),
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await service.WriteAsync(filePath, items, CancellationToken.None);
                long count = await service.GetCountAsync(filePath, CancellationToken.None);

                count.Should().Be(2);
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        [Fact]
        public async Task SearchAsync_FiltersByTitleAndTags()
        {
            string filePath = CreateUniqueTempFilePath();

            try
            {
                ParquetDataService service = new ParquetDataService();

                List<DatasetItemDto> items = new List<DatasetItemDto>
                {
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = Guid.NewGuid(),
                        ExternalId = "item-1",
                        Title = "Mountain view",
                        Tags = new List<string> { "nature" },
                        Width = 1,
                        Height = 1,
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new DatasetItemDto
                    {
                        Id = Guid.NewGuid(),
                        DatasetId = Guid.NewGuid(),
                        ExternalId = "item-2",
                        Title = "City skyline",
                        Tags = new List<string> { "city" },
                        Width = 1,
                        Height = 1,
                        Metadata = new Dictionary<string, string>(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await service.WriteAsync(filePath, items, CancellationToken.None);

                PagedResult<DatasetItemDto> result = await service.SearchAsync(filePath, "mountain", 0, 10, CancellationToken.None);

                result.TotalCount.Should().Be(1);
                result.Items.Count.Should().Be(1);
                DatasetItemDto item = result.Items[0];
                item.Title.Should().Be("Mountain view");
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
    }
}
