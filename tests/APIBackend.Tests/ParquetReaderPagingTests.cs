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
using Microsoft.Extensions.Logging.Abstractions;
using Parquet;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 4.1 (audit P-1, P-6, P-10): pages skip whole row groups before the cursor, a page that
    /// fills exactly at the end emits no cursor, shards are Snappy-compressed, and the cached
    /// row count follows appends.
    /// </summary>
    public sealed class ParquetReaderPagingTests : IDisposable
    {
        private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ParquetReaderPaging", Guid.NewGuid().ToString("N"));
        private readonly ParquetItemRepository _repository;
        private readonly Guid _datasetId = Guid.NewGuid();

        public ParquetReaderPagingTests()
        {
            Directory.CreateDirectory(_dataDirectory);
            _repository = new ParquetItemRepository(_dataDirectory, NullLogger<ParquetItemRepository>.Instance);
        }

        public void Dispose()
        {
            _repository.Dispose();
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }

        private static List<DatasetItemDto> Items(Guid datasetId, int start, int count) =>
            Enumerable.Range(start, count).Select(i => new DatasetItemDto
            {
                Id = Guid.NewGuid(),
                DatasetId = datasetId,
                ExternalId = $"row-{i:D5}",
                Title = $"item {i}",
                Tags = new List<string> { i % 2 == 0 ? "even" : "odd" },
                Metadata = new Dictionary<string, string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).ToList();

        /// <summary>Appends <paramref name="groups"/> row groups of <paramref name="groupSize"/> rows each.</summary>
        private async Task WriteGroupsAsync(int groups, int groupSize)
        {
            for (int g = 0; g < groups; g++)
            {
                await _repository.AddRangeAsync(_datasetId, Items(_datasetId, g * groupSize, groupSize), CancellationToken.None);
            }
        }

        private async Task<List<string>> ReadAllPagesAsync(int pageSize, FilterRequest? filter = null)
        {
            List<string> seen = new();
            string? cursor = null;
            int pages = 0;
            do
            {
                (IReadOnlyList<DatasetItemDto> items, string? next) = await _repository.GetPageAsync(_datasetId, filter, cursor, pageSize, CancellationToken.None);
                items.Should().NotBeEmpty("a cursor must never lead to an empty page");
                seen.AddRange(items.Select(i => i.ExternalId!));
                cursor = next;
                pages++;
                pages.Should().BeLessThan(1_000);
            }
            while (cursor is not null);
            return seen;
        }

        /// <summary>Audit P-10: 200 rows read 100 at a time is two pages, not two plus an empty third.</summary>
        [Fact]
        public async Task ExactFillAtTheEnd_EmitsNoCursor()
        {
            await WriteGroupsAsync(groups: 2, groupSize: 100);

            (IReadOnlyList<DatasetItemDto> first, string? firstCursor) = await _repository.GetPageAsync(_datasetId, null, null, 100, CancellationToken.None);
            (IReadOnlyList<DatasetItemDto> second, string? secondCursor) = await _repository.GetPageAsync(_datasetId, null, firstCursor, 100, CancellationToken.None);

            first.Should().HaveCount(100);
            firstCursor.Should().NotBeNull();
            second.Should().HaveCount(100);
            secondCursor.Should().BeNull("there are no rows after the second page");
        }

        /// <summary>
        /// Audit P-1: pages that start inside or after skipped row groups return every row once, in
        /// order, with page boundaries that don't line up with group boundaries.
        /// </summary>
        [Theory]
        [InlineData(7)]
        [InlineData(30)]
        [InlineData(45)]
        public async Task PagingAcrossManyRowGroups_ReturnsEveryRowOnceInOrder(int pageSize)
        {
            await WriteGroupsAsync(groups: 6, groupSize: 30);

            List<string> seen = await ReadAllPagesAsync(pageSize);

            seen.Should().Equal(Enumerable.Range(0, 180).Select(i => $"row-{i:D5}"));
        }

        /// <summary>Group skipping counts rows examined, not rows matched, so a filtered read still resumes in the right place.</summary>
        [Fact]
        public async Task FilteredPagingAcrossRowGroups_ReturnsEachMatchOnce()
        {
            await WriteGroupsAsync(groups: 5, groupSize: 20);

            List<string> seen = await ReadAllPagesAsync(pageSize: 9, new FilterRequest { Tags = new[] { "odd" } });

            seen.Should().Equal(Enumerable.Range(0, 100).Where(i => i % 2 == 1).Select(i => $"row-{i:D5}"));
        }

        /// <summary>
        /// Audit P-1: a cursor past the first groups doesn't decode them. The first group is
        /// truncated mid-data; a read that decoded it would throw.
        /// </summary>
        [Fact]
        public async Task PageAfterTheCursor_DoesNotDecodeEarlierRowGroups()
        {
            await WriteGroupsAsync(groups: 3, groupSize: 50);
            (_, string? cursor) = await _repository.GetPageAsync(_datasetId, null, null, 100, CancellationToken.None);
            string shard = Directory.GetFiles(_dataDirectory, "*.parquet").Single();

            await using (FileStream file = File.Open(shard, FileMode.Open, FileAccess.ReadWrite))
            using (ParquetReader reader = await ParquetReader.CreateAsync(file, leaveStreamOpen: true))
            {
                // Overwrite the first row group's column data (everything before the second group).
                long secondGroupStart = reader.Metadata!.RowGroups[1].Columns[0].MetaData!.DataPageOffset;
                long firstGroupStart = reader.Metadata.RowGroups[0].Columns[0].MetaData!.DataPageOffset;
                file.Position = firstGroupStart;
                await file.WriteAsync(new byte[secondGroupStart - firstGroupStart]);
            }

            (IReadOnlyList<DatasetItemDto> items, _) = await new ParquetItemReader(_dataDirectory).ReadPageAsync(_datasetId, null, cursor, 50);

            items.Select(i => i.ExternalId).Should().Equal(Enumerable.Range(100, 50).Select(i => $"row-{i:D5}"));
        }

        /// <summary>Audit P-6: shards are written Snappy-compressed.</summary>
        [Fact]
        public async Task Shards_AreSnappyCompressed()
        {
            await WriteGroupsAsync(groups: 2, groupSize: 10);
            string shard = Directory.GetFiles(_dataDirectory, "*.parquet").Single();

            await using FileStream file = File.OpenRead(shard);
            using ParquetReader reader = await ParquetReader.CreateAsync(file);

            reader.Metadata!.RowGroups.SelectMany(g => g.Columns)
                .Should().OnlyContain(c => c.MetaData!.Codec == Parquet.Meta.CompressionCodec.SNAPPY);
        }

        /// <summary>The cached row count follows appends to the shard.</summary>
        [Fact]
        public async Task CountAsync_FollowsAppends()
        {
            ParquetItemReader reader = new(_dataDirectory);
            await WriteGroupsAsync(groups: 1, groupSize: 10);
            (await reader.CountAsync(_datasetId)).Should().Be(10);

            await _repository.AddRangeAsync(_datasetId, Items(_datasetId, 10, 5), CancellationToken.None);

            (await reader.CountAsync(_datasetId)).Should().Be(15);
        }
    }
}
