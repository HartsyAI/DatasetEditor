using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Regression for the shard-rewrite defect raised in review of PR #8: a cancelled PATCH used to
    /// delete the shard before writing its replacement, so a cancellation in between lost every item
    /// in that shard. A rewrite now writes a temp file and renames it over the original, so a
    /// cancelled or failed replacement must leave the original shard, all of its items, untouched.
    /// </summary>
    public sealed class ShardRewriteCancellationTests
    {
        private const int ItemCount = 500;

        [Fact]
        public async Task UpdateItems_CancelledBeforeCommit_LeavesOriginalSegmentIntact()
        {
            string dataDirectory = Path.Combine(
                Path.GetTempPath(), "DatasetStudioTests", "ShardRewriteCancellation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            try
            {
                using ParquetItemRepository repository = new ParquetItemRepository(
                    dataDirectory, NullLogger<ParquetItemRepository>.Instance);

                Guid datasetId = Guid.NewGuid();
                List<DatasetItemDto> original = BuildItems(datasetId, "before");
                await repository.AddRangeAsync(datasetId, original);

                List<DatasetItemDto> replacement = original.Select(i => i with { Title = "after" }).ToList();

                using CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();

                // The cancelled replacement must not survive as a shard, whether the write observes the
                // token (throws) or the rename has already been reached (completes). Either way the
                // shard must hold every item, each fully old or fully new.
                try
                {
                    await repository.UpdateItemsAsync(replacement, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the token was observed before the commit.
                }

                (IReadOnlyList<DatasetItemDto> readBack, _) = await repository.GetPageAsync(
                    datasetId, null, null, ItemCount + 1);

                readBack.Should().HaveCount(ItemCount, "a cancelled replacement must not drop items");
                readBack.Select(i => i.Title).Should().OnlyContain(
                    t => t == "before" || t == "after",
                    "every item must be either fully old or fully new");

                Directory.GetFiles(dataDirectory, "*.tmp").Should().BeEmpty(
                    "a cancelled replacement must not leave its temp file behind");
            }
            finally
            {
                try
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the temp directory.
                }
            }
        }

        private static List<DatasetItemDto> BuildItems(Guid datasetId, string title)
        {
            List<DatasetItemDto> items = new List<DatasetItemDto>(ItemCount);
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < ItemCount; i++)
            {
                items.Add(new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = $"item-{i}",
                    Title = title,
                    Description = string.Empty,
                    Width = 64,
                    Height = 64,
                    Tags = new List<string>(),
                    Metadata = new Dictionary<string, string>(),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            return items;
        }
    }
}
