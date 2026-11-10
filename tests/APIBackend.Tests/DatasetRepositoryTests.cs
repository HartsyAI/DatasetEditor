using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// DatasetRepository against real Postgres. It used to run on the EF InMemory provider, which
    /// has no xmin concurrency token and no ExecuteUpdate, both of which the repository now relies
    /// on for patch-style writes (audit D-4).
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class DatasetRepositoryTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public DatasetRepositoryTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _ = _factory.Services;
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        /// <summary>Runs <paramref name="action"/> with a repository on its own DbContext, like one request.</summary>
        private async Task<T> WithRepositoryAsync<T>(Func<IDatasetRepository, Task<T>> action)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<IDatasetRepository>());
        }

        private Task WithRepositoryAsync(Func<IDatasetRepository, Task> action)
        {
            return WithRepositoryAsync(async repository =>
            {
                await action(repository);
                return true;
            });
        }

        private static DatasetDto NewDataset(string name) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Description",
            Format = "CSV",
            Modality = "Image",
            Status = IngestionStatusDto.Pending,
            TotalItems = 0,
            SourceType = DatasetSourceType.LocalUpload,
        };

        private async Task<DatasetDto> CreateAsync(string name)
        {
            Guid id = await WithRepositoryAsync(r => r.CreateAsync(NewDataset(name)));
            return (await WithRepositoryAsync(r => r.GetByIdAsync(id)))!;
        }

        private Task<DatasetDto?> GetAsync(Guid id) => WithRepositoryAsync(r => r.GetByIdAsync(id));

        [Fact]
        public async Task CreateAndGetAsync_PersistsDataset()
        {
            DatasetDto loaded = await CreateAsync("Test dataset");

            loaded.Name.Should().Be("Test dataset");
            loaded.Description.Should().Be("Description");
        }

        [Fact]
        public async Task GetAllAsync_ReturnsDatasetsOrderedByCreatedAtDescending()
        {
            // CreateAsync stamps CreatedAt = UtcNow server-side, so order the writes themselves.
            await CreateAsync("Older");
            await Task.Delay(TimeSpan.FromMilliseconds(5));
            await CreateAsync("Newer");

            List<DatasetDto> list = await WithRepositoryAsync(r => r.GetAllAsync(page: 0, pageSize: 50));

            list.Should().HaveCount(2);
            list[0].Name.Should().Be("Newer");
            list[1].Name.Should().Be("Older");
        }

        /// <summary>
        /// Regression: under the DbContext's NoTracking default, status writes used to fetch an
        /// untracked entity and save nothing, so datasets stayed Pending forever.
        /// </summary>
        [Fact]
        public async Task UpdateStatusAsync_PersistsTheNewStatus()
        {
            DatasetDto dataset = await CreateAsync("Status test");

            await WithRepositoryAsync(r => r.UpdateStatusAsync(dataset.Id, IngestionStatusDto.Failed, "boom"));

            DatasetDto loaded = (await GetAsync(dataset.Id))!;
            loaded.Status.Should().Be(IngestionStatusDto.Failed);
            loaded.ErrorMessage.Should().Be("boom");
        }

        [Fact]
        public async Task UpdateItemCountAsync_PersistsTheNewCount()
        {
            DatasetDto dataset = await CreateAsync("Count test");

            await WithRepositoryAsync(r => r.UpdateItemCountAsync(dataset.Id, 12345));

            (await GetAsync(dataset.Id))!.TotalItems.Should().Be(12345);
        }

        [Fact]
        public async Task UpdateStatusAsync_MissingDataset_Throws()
        {
            Func<Task> act = () => WithRepositoryAsync(r => r.UpdateStatusAsync(Guid.NewGuid(), IngestionStatusDto.Completed));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task DeleteAsync_RemovesDataset()
        {
            DatasetDto dataset = await CreateAsync("ToDelete");

            await WithRepositoryAsync(r => r.DeleteAsync(dataset.Id));

            (await GetAsync(dataset.Id)).Should().BeNull();
        }

        /// <summary>
        /// Audit D-4: an update built from a snapshot read before background ingestion wrote its
        /// status and count must not revert them. The old full-row overwrite put the dataset back
        /// to Pending with 0 items.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_KeepsStatusAndCountWrittenAfterTheSnapshot()
        {
            DatasetDto snapshot = await CreateAsync("Snapshot");
            await WithRepositoryAsync(r => r.UpdateStatusAsync(snapshot.Id, IngestionStatusDto.Processing));
            await WithRepositoryAsync(r => r.UpdateItemCountAsync(snapshot.Id, 5_000));

            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { SourceFileName = "rows.csv" }));

            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            loaded.SourceFileName.Should().Be("rows.csv");
            loaded.Status.Should().Be(IngestionStatusDto.Processing, "the background status write must survive");
            loaded.TotalItems.Should().Be(5_000, "the background count write must survive");
        }

        /// <summary>A field another request changed to a different value is a conflict, and nothing is written.</summary>
        [Fact]
        public async Task UpdateAsync_FieldChangedByAnotherRequest_ThrowsConflictAndWritesNothing()
        {
            DatasetDto snapshot = await CreateAsync("Original");
            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { Name = "Renamed elsewhere" }));

            Func<Task> act = () => WithRepositoryAsync(r =>
                r.UpdateAsync(snapshot, snapshot with { Name = "Renamed here", Description = "new description" }));

            (await act.Should().ThrowAsync<DatasetUpdateConflictException>()).Which.Fields.Should().Equal("Name");
            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            loaded.Name.Should().Be("Renamed elsewhere");
            loaded.Description.Should().Be("Description", "a conflicting update is all-or-nothing");
        }

        /// <summary>Two requests writing the same value is not a conflict.</summary>
        [Fact]
        public async Task UpdateAsync_SameValueWrittenConcurrently_IsNotAConflict()
        {
            DatasetDto snapshot = await CreateAsync("Streaming");
            DatasetDto resolved = snapshot with { HuggingFaceConfig = "default", HuggingFaceSplit = "train" };
            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, resolved));

            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, resolved));

            (await GetAsync(snapshot.Id))!.HuggingFaceSplit.Should().Be("train");
        }

        /// <summary>
        /// Codex review on #21: re-importing from a new repository after a concurrent read resolved
        /// the old repository's config/split is refused, rather than storing the new repository
        /// with the old one's split.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_NewRepositoryAfterConcurrentResolution_IsRefused()
        {
            DatasetDto snapshot = await CreateAsync("Re-import");
            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { HuggingFaceRepository = "org/old" }));
            snapshot = (await GetAsync(snapshot.Id))!;
            // A concurrent items request resolves the old repository's config/split.
            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { HuggingFaceConfig = "old-config", HuggingFaceSplit = "old-split" }));

            Func<Task> act = () => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { HuggingFaceRepository = "org/new" }));

            (await act.Should().ThrowAsync<DatasetUpdateConflictException>()).Which.Fields
                .Should().BeEquivalentTo("HuggingFaceConfig", "HuggingFaceSplit");
            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            (loaded.HuggingFaceRepository, loaded.HuggingFaceConfig).Should().Be(("org/old", "old-config"), "a refused update writes nothing");
        }

        /// <summary>
        /// Codex review on #21: two S3 connects from the same snapshot, one changing the profile and
        /// one the bucket, can't merge into a pair neither validated. The second is refused.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_ConcurrentS3Selections_NeverMixProfileAndBucket()
        {
            DatasetDto start = await CreateAsync("S3");
            await WithRepositoryAsync(r => r.UpdateAsync(start, start with { S3StorageProfileName = "A", S3Bucket = "X" }));
            DatasetDto snapshot = (await GetAsync(start.Id))!;

            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { S3StorageProfileName = "B" }));
            Func<Task> second = () => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { S3Bucket = "Y" }));

            await second.Should().ThrowAsync<DatasetUpdateConflictException>();
            DatasetDto loaded = (await GetAsync(start.Id))!;
            (loaded.S3StorageProfileName, loaded.S3Bucket).Should().Be(("B", "X"), "B/Y was never validated by either request");
        }

        /// <summary>
        /// Codex review on #21: an S3 reconnect racing a switch to HuggingFace streaming is refused,
        /// so the dataset never ends up typed HuggingFace with an S3 bucket the caller thought it set.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_S3ReconnectAfterProviderSwitch_IsRefused()
        {
            DatasetDto start = await CreateAsync("Provider switch");
            await WithRepositoryAsync(r => r.UpdateAsync(start, start with
            {
                SourceType = DatasetSourceType.ExternalS3Streaming, IsStreaming = true, S3StorageProfileName = "A", S3Bucket = "X",
            }));
            DatasetDto snapshot = (await GetAsync(start.Id))!;

            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with
            {
                SourceType = DatasetSourceType.HuggingFaceStreaming, HuggingFaceRepository = "org/ds",
            }));
            Func<Task> reconnect = () => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { S3Bucket = "Y" }));

            await reconnect.Should().ThrowAsync<DatasetUpdateConflictException>();
            DatasetDto loaded = (await GetAsync(start.Id))!;
            (loaded.SourceType, loaded.S3Bucket).Should().Be((DatasetSourceType.HuggingFaceStreaming, "X"));
        }

        /// <summary>A source change with no concurrent source change is written as usual.</summary>
        [Fact]
        public async Task UpdateAsync_SourceChangeWithoutRace_IsWritten()
        {
            DatasetDto snapshot = await CreateAsync("No race");
            await WithRepositoryAsync(r => r.UpdateItemCountAsync(snapshot.Id, 7));

            await WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { HuggingFaceRepository = "org/ds", HuggingFaceSplit = "train" }));

            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            (loaded.HuggingFaceRepository, loaded.HuggingFaceSplit, loaded.TotalItems).Should().Be(("org/ds", "train", 7L));
        }

        /// <summary>
        /// Twelve writers patching different fields (plus status and count writes) at once all land.
        /// With three token retries, a patch that conflicted with nothing could exhaust them and fail
        /// with a false conflict; this was seen as an intermittent failure of the five-writer test below.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_HeavyContentionOnDifferentFields_NoFalseConflicts()
        {
            DatasetDto snapshot = await CreateAsync("Contention");

            List<Task> writers = new();
            for (int round = 0; round < 3; round++)
            {
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { SourceFileName = "a.csv" }))));
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { Description = "described" }))));
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { IsPublic = true }))));
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { Format = "CSV2" }))));
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateItemCountAsync(snapshot.Id, 42))));
                writers.Add(Task.Run(() => WithRepositoryAsync(r => r.UpdateStatusAsync(snapshot.Id, IngestionStatusDto.Processing))));
            }

            await Task.WhenAll(writers);

            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            (loaded.SourceFileName, loaded.Description, loaded.IsPublic, loaded.Format).Should().Be(("a.csv", "described", true, "CSV2"));
        }

        /// <summary>Many concurrent patches of different fields all land.</summary>
        [Fact]
        public async Task UpdateAsync_ConcurrentPatchesOfDifferentFields_AllLand()
        {
            DatasetDto snapshot = await CreateAsync("Concurrent");

            await Task.WhenAll(
                Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { SourceFileName = "a.csv" }))),
                Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { Description = "described" }))),
                Task.Run(() => WithRepositoryAsync(r => r.UpdateAsync(snapshot, snapshot with { IsPublic = true }))),
                Task.Run(() => WithRepositoryAsync(r => r.UpdateItemCountAsync(snapshot.Id, 42))),
                Task.Run(() => WithRepositoryAsync(r => r.UpdateStatusAsync(snapshot.Id, IngestionStatusDto.Processing))));

            DatasetDto loaded = (await GetAsync(snapshot.Id))!;
            loaded.SourceFileName.Should().Be("a.csv");
            loaded.Description.Should().Be("described");
            loaded.IsPublic.Should().BeTrue();
            loaded.TotalItems.Should().Be(42);
            loaded.Status.Should().Be(IngestionStatusDto.Processing);
        }
    }
}
