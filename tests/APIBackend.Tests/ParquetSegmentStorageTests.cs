using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// PR 4.2 (audit P-2, P-3, P-4, P-7, P-11, rest of P-5): items live in immutable segments
    /// committed by a manifest swap. A write that dies part way never damages committed data;
    /// items are found by id index; edits rewrite only their segment; counts survive restarts;
    /// small segments are merged; shards from before manifests are adopted.
    /// </summary>
    public sealed class ParquetSegmentStorageTests : IDisposable
    {
        private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "ParquetSegments", Guid.NewGuid().ToString("N"));
        private readonly Guid _datasetId = Guid.NewGuid();
        private ParquetItemRepository _repository;

        public ParquetSegmentStorageTests()
        {
            Directory.CreateDirectory(_dataDirectory);
            _repository = NewRepository();
        }

        public void Dispose()
        {
            _repository.Dispose();
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }

        private ParquetItemRepository NewRepository() => new(_dataDirectory, NullLogger<ParquetItemRepository>.Instance);

        private List<DatasetItemDto> Items(int start, int count, string title = "item") =>
            Enumerable.Range(start, count).Select(i => new DatasetItemDto
            {
                Id = Guid.NewGuid(),
                DatasetId = _datasetId,
                ExternalId = $"row-{i:D5}",
                Title = $"{title} {i}",
                Tags = new List<string>(),
                Metadata = new Dictionary<string, string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).ToList();

        private async Task<List<DatasetItemDto>> ReadAllAsync(int pageSize = 37)
        {
            List<DatasetItemDto> all = new();
            string? cursor = null;
            do
            {
                (IReadOnlyList<DatasetItemDto> items, string? next) = await _repository.GetPageAsync(_datasetId, null, cursor, pageSize, CancellationToken.None);
                all.AddRange(items);
                cursor = next;
            }
            while (cursor is not null);
            return all;
        }

        private string ManifestPath => Path.Combine(_dataDirectory, ParquetManifestStore.ManifestFileName(_datasetId));

        /// <summary>
        /// Audit P-11: a write that dies part way leaves only files the manifest doesn't list.
        /// Committed items stay readable, and the next write sweeps the debris away.
        /// </summary>
        [Fact]
        public async Task WriteThatDiesPartWay_LeavesCommittedItemsReadable_AndIsSweptLater()
        {
            await _repository.AddRangeAsync(_datasetId, Items(0, 20), CancellationToken.None);
            // What a killed append leaves: a half-written temp file and a torn, unpublished segment.
            string torn = Path.Combine(_dataDirectory, ParquetManifestStore.SegmentFileName(_datasetId, 99));
            await File.WriteAllBytesAsync(torn, new byte[] { 0x50, 0x41, 0x52, 0x31, 0x00 });
            await File.WriteAllBytesAsync(torn + ".abc.tmp", new byte[] { 0x01, 0x02 });

            _repository.Dispose();
            _repository = NewRepository(); // a restart

            (await ReadAllAsync()).Select(i => i.ExternalId).Should().Equal(Enumerable.Range(0, 20).Select(i => $"row-{i:D5}"));

            await _repository.AddRangeAsync(_datasetId, Items(20, 5), CancellationToken.None);
            File.Exists(torn).Should().BeFalse("unpublished files are swept on the next write");
            File.Exists(torn + ".abc.tmp").Should().BeFalse();
            (await _repository.GetCountAsync(_datasetId)).Should().Be(25);
        }

        /// <summary>A segment write that fails part way leaves nothing under the segment's name.</summary>
        [Fact]
        public async Task SegmentWrite_FailingPartWay_LeavesNoFileUnderItsName()
        {
            string path = Path.Combine(_dataDirectory, "dataset_x_seg_00000001.parquet");

            Func<Task> act = () => new ParquetItemWriter(_dataDirectory).WriteSegmentAsync(path, FailAfterOneGroup(), CancellationToken.None);

            await act.Should().ThrowAsync<IOException>();
            File.Exists(path).Should().BeFalse();
            Directory.GetFiles(_dataDirectory, "*.tmp").Should().BeEmpty();
        }

        private async IAsyncEnumerable<List<DatasetItemDto>> FailAfterOneGroup([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Items(0, 10);
            await Task.Yield();
            throw new IOException("disk went away");
        }

        /// <summary>Audit P-2: items are found by id within their dataset, and without a dataset hint.</summary>
        [Fact]
        public async Task GetItem_FindsItemsAcrossSegments_WithAndWithoutTheDatasetId()
        {
            List<DatasetItemDto> first = Items(0, 30);
            List<DatasetItemDto> second = Items(30, 30);
            await _repository.AddRangeAsync(_datasetId, first, CancellationToken.None);
            await _repository.AddRangeAsync(_datasetId, second, CancellationToken.None);

            (await _repository.GetItemAsync(_datasetId, second[17].Id, CancellationToken.None))!.ExternalId.Should().Be("row-00047");
            (await _repository.GetItemAsync(first[3].Id, CancellationToken.None))!.ExternalId.Should().Be("row-00003");
            (await _repository.GetItemAsync(_datasetId, Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        }

        /// <summary>
        /// Audit P-3: an edit rewrites only the segment that holds the item. The other segment's
        /// file is not touched, and the edited item is still found by id afterwards.
        /// </summary>
        [Fact]
        public async Task UpdateItems_RewritesOnlyTheSegmentHoldingTheItem()
        {
            List<DatasetItemDto> first = Items(0, 30);
            List<DatasetItemDto> second = Items(30, 30);
            await _repository.AddRangeAsync(_datasetId, first, CancellationToken.None);
            await _repository.AddRangeAsync(_datasetId, second, CancellationToken.None);
            string firstSegment = Path.Combine(_dataDirectory, ParquetManifestStore.SegmentFileName(_datasetId, 0));
            DateTime firstWrittenAt = File.GetLastWriteTimeUtc(firstSegment);
            await Task.Delay(50);

            await _repository.UpdateItemsAsync(new[] { second[5] with { Title = "edited" } }, CancellationToken.None);

            File.GetLastWriteTimeUtc(firstSegment).Should().Be(firstWrittenAt, "the segment without edits isn't rewritten");
            (await _repository.GetItemAsync(_datasetId, second[5].Id, CancellationToken.None))!.Title.Should().Be("edited");
            (await ReadAllAsync()).Select(i => i.ExternalId).Should().Equal(Enumerable.Range(0, 60).Select(i => $"row-{i:D5}"));
        }

        /// <summary>Audit P-7: the count comes from the manifest, so a new instance appends after the existing rows.</summary>
        [Fact]
        public async Task CountAndAppendOrder_SurviveARestart()
        {
            await _repository.AddRangeAsync(_datasetId, Items(0, 12), CancellationToken.None);
            _repository.Dispose();
            _repository = NewRepository();

            (await _repository.GetCountAsync(_datasetId)).Should().Be(12);
            await _repository.AddRangeAsync(_datasetId, Items(12, 3), CancellationToken.None);

            (await ReadAllAsync()).Select(i => i.ExternalId).Should().Equal(Enumerable.Range(0, 15).Select(i => $"row-{i:D5}"));
        }

        /// <summary>
        /// Rest of audit P-5: many small appends are merged, keeping row order, and a cursor taken
        /// before the merge still continues from the same row.
        /// </summary>
        [Fact]
        public async Task SmallAppends_AreMerged_AndCursorsSurviveTheMerge()
        {
            for (int i = 0; i < ParquetItemRepository.CompactionMinSegments - 1; i++)
            {
                await _repository.AddRangeAsync(_datasetId, Items(i * 10, 10), CancellationToken.None);
            }
            (IReadOnlyList<DatasetItemDto> firstPage, string? cursor) = await _repository.GetPageAsync(_datasetId, null, null, 25, CancellationToken.None);

            // This append makes the eighth small segment and triggers the merge.
            await _repository.AddRangeAsync(_datasetId, Items(70, 10), CancellationToken.None);

            Directory.GetFiles(_dataDirectory, "*.parquet").Should().ContainSingle("eight small segments were merged into one");
            List<DatasetItemDto> rest = new();
            while (cursor is not null)
            {
                (IReadOnlyList<DatasetItemDto> items, string? next) = await _repository.GetPageAsync(_datasetId, null, cursor, 25, CancellationToken.None);
                rest.AddRange(items);
                cursor = next;
            }
            firstPage.Concat(rest).Select(i => i.ExternalId).Should().Equal(Enumerable.Range(0, 80).Select(i => $"row-{i:D5}"));
            (await _repository.GetItemAsync(_datasetId, firstPage[3].Id, CancellationToken.None)).Should().NotBeNull("the merged segment has an id index");
        }

        /// <summary>
        /// Data written before manifests (one shard file, appended in place) is adopted on first read
        /// without being rewritten, and an old "shard:row" cursor still works.
        /// </summary>
        [Fact]
        public async Task LegacyShards_AreAdopted_AndOldCursorsStillWork()
        {
            string shard = Path.Combine(_dataDirectory, ParquetSchemaDefinition.GetShardFileName(_datasetId, 0));
            List<DatasetItemDto> items = Items(0, 40);
            await new ParquetItemWriter(_dataDirectory).WriteSegmentAsync(shard, items, CancellationToken.None);
            DateTime writtenAt = File.GetLastWriteTimeUtc(shard);

            (await _repository.GetCountAsync(_datasetId)).Should().Be(40);
            (IReadOnlyList<DatasetItemDto> page, _) = await _repository.GetPageAsync(_datasetId, null, "0:35", 10, CancellationToken.None);
            (await _repository.GetItemAsync(_datasetId, items[21].Id, CancellationToken.None))!.ExternalId.Should().Be("row-00021");

            page.Select(i => i.ExternalId).Should().Equal(Enumerable.Range(35, 5).Select(i => $"row-{i:D5}"));
            File.Exists(ManifestPath).Should().BeTrue();
            File.GetLastWriteTimeUtc(shard).Should().Be(writtenAt, "adoption doesn't rewrite the data");
        }

        /// <summary>Deleting a dataset removes its manifest and every file.</summary>
        [Fact]
        public async Task DeleteByDataset_RemovesEveryFile()
        {
            await _repository.AddRangeAsync(_datasetId, Items(0, 10), CancellationToken.None);

            await _repository.DeleteByDatasetAsync(_datasetId, CancellationToken.None);

            Directory.GetFiles(_dataDirectory, $"dataset_{_datasetId:N}*").Should().BeEmpty();
            (await _repository.GetCountAsync(_datasetId)).Should().Be(0);
        }
    }
}
