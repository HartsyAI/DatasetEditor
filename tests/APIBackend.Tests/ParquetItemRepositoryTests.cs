using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.DTO.Common;
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

                long count = await repository.GetCountAsync(datasetId, CancellationToken.None);
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

        private static DatasetItemDto MakeItem(Guid datasetId, string externalId) => new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            ExternalId = externalId,
            Title = externalId,
            Width = 10,
            Height = 5,
            Tags = new List<string>(),
            Metadata = new Dictionary<string, string>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Regression test for the Parquet append-path bug: AppendToFileAsync used to open the
        // shard file with FileMode.Append, which rejects the backward seek Parquet's footer
        // rewrite needs on append: true. Any second AddRangeAsync call into an already-written
        // shard exercises this path (the first call creates the file; every call after that
        // appends to it).
        [Fact]
        public async Task AddRangeAsync_CalledTwice_AppendsToExistingShardWithoutCorruption()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);
                Guid datasetId = Guid.NewGuid();

                await repository.AddRangeAsync(
                    datasetId,
                    new List<DatasetItemDto> { MakeItem(datasetId, "first-batch-1"), MakeItem(datasetId, "first-batch-2") },
                    CancellationToken.None);

                // This second call is what forces the append path: the shard file created by
                // the call above already exists on disk.
                await repository.AddRangeAsync(
                    datasetId,
                    new List<DatasetItemDto> { MakeItem(datasetId, "second-batch-1") },
                    CancellationToken.None);

                (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) page =
                    await repository.GetPageAsync(datasetId, null, null, 10, CancellationToken.None);

                page.Items.Count.Should().Be(3);
                page.Items.Select(i => i.ExternalId).Should().BeEquivalentTo(
                    new[] { "first-batch-1", "first-batch-2", "second-batch-1" });

                long count = await repository.GetCountAsync(datasetId, CancellationToken.None);
                count.Should().Be(3);
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }

        // A single AddRangeAsync call with more items than DefaultBatchSize (10,000) forces
        // ParquetItemRepository's own internal batch loop through multiple WriteBatchAsync
        // calls landing in the same shard — so the append path is exercised within one logical
        // ingestion, not just across two separate calls.
        [Fact]
        public async Task AddRangeAsync_ExceedingBatchSize_WritesAllItemsAcrossMultipleAppends()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);
                Guid datasetId = Guid.NewGuid();

                const int totalItems = ParquetSchemaDefinition.DefaultBatchSize * 2 + 500; // forces 3 internal batches
                List<DatasetItemDto> items = new List<DatasetItemDto>(totalItems);
                for (int i = 0; i < totalItems; i++)
                {
                    items.Add(MakeItem(datasetId, $"item-{i:D6}"));
                }

                await repository.AddRangeAsync(datasetId, items, CancellationToken.None);

                long count = await repository.GetCountAsync(datasetId, CancellationToken.None);
                count.Should().Be(totalItems);

                // Spot-check content survived the append path intact, not just the count —
                // read the last page (near the end of the shard, written by the final,
                // appended batch) and confirm the items are the expected ones, in order.
                (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) firstPage =
                    await repository.GetPageAsync(datasetId, null, null, totalItems, CancellationToken.None);

                firstPage.Items.Count.Should().Be(totalItems);
                firstPage.Items[0].ExternalId.Should().Be("item-000000");
                firstPage.Items[^1].ExternalId.Should().Be($"item-{totalItems - 1:D6}");
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }

        // Regression test: InitializeItemCounts() used to pre-seed every dataset discovered
        // on disk at startup with a count of 0 "to be computed on first access" — but that 0
        // IS the cached value from then on (GetCountAsync's fast path is a plain
        // TryGetValue), so a fresh repository instance pointed at a data directory with
        // existing items (simulating a service restart) would report 0 items forever, and a
        // subsequent AddRangeAsync would compute new items' starting index from 0 too,
        // colliding with the dataset's real existing indices.
        [Fact]
        public async Task NewRepositoryInstance_OverExistingData_ReportsRealCountNotZero()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                Guid datasetId = Guid.NewGuid();
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;

                using (ParquetItemRepository firstInstance = new ParquetItemRepository(dataDirectory, logger))
                {
                    await firstInstance.AddRangeAsync(
                        datasetId,
                        new List<DatasetItemDto> { MakeItem(datasetId, "existing-1"), MakeItem(datasetId, "existing-2") },
                        CancellationToken.None);
                }

                // A brand new instance over the same directory — what the API process does on
                // every restart, since ParquetItemRepository is registered as a singleton.
                using ParquetItemRepository restartedInstance = new ParquetItemRepository(dataDirectory, logger);

                long count = await restartedInstance.GetCountAsync(datasetId, CancellationToken.None);
                count.Should().Be(2);

                // And a subsequent write picks up from the real count, not from 0.
                await restartedInstance.AddRangeAsync(
                    datasetId,
                    new List<DatasetItemDto> { MakeItem(datasetId, "after-restart-1") },
                    CancellationToken.None);

                (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) page =
                    await restartedInstance.GetPageAsync(datasetId, null, null, 10, CancellationToken.None);
                page.Items.Count.Should().Be(3);
                page.Items.Select(i => i.ExternalId).Should().BeEquivalentTo(
                    new[] { "existing-1", "existing-2", "after-restart-1" });
            }
            finally
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, true);
                }
            }
        }

        // Regression test for the filter-cursor undercounting bug: ReadPageAsync used to derive
        // the next cursor's row index from the number of items that PASSED the filter, not the
        // number of rows actually scanned to produce them. With a selective filter that leaves
        // large gaps between matches, that undercount made the next page's cursor point earlier
        // than where the previous page really finished — re-scanning (and re-returning) rows
        // already consumed. Paging through with a sparse filter and pageSize smaller than the
        // total match count is exactly the condition that exercises this.
        [Fact]
        public async Task GetPageAsync_WithSparseFilter_PagesWithoutDuplicatesOrGaps()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);
                Guid datasetId = Guid.NewGuid();

                // 30 items, only every 3rd one favorited (10 matches spread across the shard
                // with 2 non-matching rows between each) — sparse enough that a 2-item page
                // never lines up with the raw row boundary the old bug computed.
                List<DatasetItemDto> items = new List<DatasetItemDto>();
                List<string> expectedFavoriteIds = new List<string>();
                for (int i = 0; i < 30; i++)
                {
                    bool isFavorite = i % 3 == 0;
                    string externalId = $"item-{i:D3}";
                    items.Add(MakeItem(datasetId, externalId) with { IsFavorite = isFavorite });
                    if (isFavorite)
                    {
                        expectedFavoriteIds.Add(externalId);
                    }
                }
                await repository.AddRangeAsync(datasetId, items, CancellationToken.None);

                FilterRequest filter = new() { FavoritesOnly = true };
                List<string> collectedIds = new List<string>();
                string? cursor = null;
                int safetyLimit = 20; // 10 matches / pageSize 2 = 5 pages; generous margin over that

                do
                {
                    (IReadOnlyList<DatasetItemDto> Items, string? NextCursor) page =
                        await repository.GetPageAsync(datasetId, filter, cursor, pageSize: 2, CancellationToken.None);
                    collectedIds.AddRange(page.Items.Select(i => i.ExternalId!));
                    cursor = page.NextCursor;
                    safetyLimit--;
                }
                while (cursor != null && safetyLimit > 0);

                safetyLimit.Should().BeGreaterThan(0, "pagination should have terminated via a null cursor, not the safety limit");
                collectedIds.Should().BeEquivalentTo(expectedFavoriteIds, options => options.WithoutStrictOrdering());
                collectedIds.Should().OnlyHaveUniqueItems("the old bug could return the same row twice across pages");
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
                long count = await repository.GetCountAsync(datasetId, CancellationToken.None);
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

        /// <summary>
        /// Phase 6 real-run regression: AddRangeAsync's writer opens each shard with
        /// File.Open(path, FileMode.Open, FileAccess.ReadWrite) -- that overload defaults to
        /// FileShare.None (exclusive), needed for ParquetWriter's backward-seeking footer
        /// rewrite. GetPageAsync/GetItemAsync/GetCountAsync/GetStatisticsAsync didn't take the
        /// same per-dataset lock the writer does, so a read landing while a write held that
        /// exclusive handle threw "the process cannot access the file because it is being used
        /// by another process" -- hit live via a real background-ingestion-then-immediate-read
        /// sequence through a production Docker deployment, not a synthetic worst case. This
        /// fires many concurrent writes and reads against the same dataset to reproduce the
        /// race reliably rather than relying on exact timing.
        /// </summary>
        [Fact]
        public async Task ConcurrentWritesAndReads_SameDataset_NeverThrowFileShareException()
        {
            string dataDirectory = CreateUniqueDataDirectory();

            try
            {
                ILogger<ParquetItemRepository> logger = NullLogger<ParquetItemRepository>.Instance;
                using ParquetItemRepository repository = new ParquetItemRepository(dataDirectory, logger);

                Guid datasetId = Guid.NewGuid();

                static DatasetItemDto MakeItem(Guid datasetId, int i) => new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = datasetId,
                    ExternalId = $"item-{i}",
                    Title = $"Item {i}",
                    Width = 10,
                    Height = 10,
                    Tags = new List<string>(),
                    Metadata = new Dictionary<string, string>(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Seed once so the early reads have something real to read, not just an
                // empty/not-yet-created shard.
                await repository.AddRangeAsync(datasetId, new List<DatasetItemDto> { MakeItem(datasetId, -1) }, CancellationToken.None);

                List<Task> operations = new();
                for (int i = 0; i < 20; i++)
                {
                    int captured = i;
                    operations.Add(repository.AddRangeAsync(datasetId, new List<DatasetItemDto> { MakeItem(datasetId, captured) }, CancellationToken.None));
                    operations.Add(repository.GetPageAsync(datasetId, null, null, 50, CancellationToken.None));
                    operations.Add(repository.GetCountAsync(datasetId, CancellationToken.None));
                    operations.Add(repository.GetItemAsync(Guid.NewGuid(), CancellationToken.None)); // not found, but exercises the same locked read path
                }

                Func<Task> act = () => Task.WhenAll(operations);

                await act.Should().NotThrowAsync(
                    "reads must serialize against the writer's exclusive file handle instead of racing it");
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
