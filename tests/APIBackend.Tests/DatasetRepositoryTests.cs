using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    // NOTE: this file previously tested a DatasetEntity-in/DatasetEntity-out shape
    // (CreateAsync(DatasetEntity), GetAsync, ListAsync) that never matched the real
    // DatasetRepository (Core.Abstractions.Repositories.IDatasetRepository), which has
    // always been DatasetDto-in/DatasetDto-out with CreateAsync returning a Guid,
    // GetByIdAsync, and GetAllAsync. It could not have compiled — evidence this suite
    // was never actually run. Rewritten against the real API.
    public sealed class DatasetRepositoryTests
    {
        private static DatasetStudioDbContext CreateInMemoryContext()
        {
            DbContextOptionsBuilder<DatasetStudioDbContext> builder = new DbContextOptionsBuilder<DatasetStudioDbContext>();
            builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            // Mirrors the real DbContext config (ServiceCollectionExtensions.AddDatasetServices)
            // exactly — this default is what made the UpdateAsync/UpdateStatusAsync/
            // UpdateItemCountAsync fetch-mutate-SaveChanges pattern silently no-op in
            // production. A prior version of this test helper didn't set it, so it couldn't
            // have caught that bug even though the repository's tests otherwise looked fine.
            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            DatasetStudioDbContext context = new DatasetStudioDbContext(builder.Options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task CreateAndGetAsync_PersistsDataset()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetDto dataset = new()
            {
                Id = Guid.NewGuid(),
                Name = "Test dataset",
                Description = "Description",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };

            Guid createdId = await repository.CreateAsync(dataset, CancellationToken.None);
            DatasetDto? loaded = await repository.GetByIdAsync(createdId, CancellationToken.None);

            loaded.Should().NotBeNull();
            if (loaded != null)
            {
                loaded.Name.Should().Be("Test dataset");
                loaded.Description.Should().Be("Description");
            }
        }

        [Fact]
        public async Task GetAllAsync_ReturnsDatasetsOrderedByCreatedAtDescending()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetDto older = new()
            {
                Id = Guid.NewGuid(),
                Name = "Older",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };

            DatasetDto newer = new()
            {
                Id = Guid.NewGuid(),
                Name = "Newer",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };

            // CreateAsync stamps CreatedAt = UtcNow server-side, so order the writes
            // themselves rather than pre-setting timestamps the repository ignores.
            await repository.CreateAsync(older, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(5));
            await repository.CreateAsync(newer, CancellationToken.None);

            List<DatasetDto> list = await repository.GetAllAsync(page: 0, pageSize: 50, CancellationToken.None);

            list.Count.Should().Be(2);
            list[0].Name.Should().Be("Newer");
            list[1].Name.Should().Be("Older");
        }

        // Regression test: under the ambient NoTracking QueryTrackingBehavior (see
        // CreateInMemoryContext), UpdateAsync/UpdateStatusAsync/UpdateItemCountAsync used to
        // fetch an entity, mutate its properties, and call SaveChangesAsync — but the fetched
        // entity was untracked, so the mutations were invisible to EF's change tracker and
        // SaveChangesAsync had nothing to persist. Silently broke the entire ingestion-status
        // feature (datasets stayed "Pending" forever) with no exception anywhere.
        [Fact]
        public async Task UpdateStatusAsync_ActuallyPersistsTheNewStatus()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetDto dataset = new()
            {
                Id = Guid.NewGuid(),
                Name = "Status test",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };
            Guid createdId = await repository.CreateAsync(dataset, CancellationToken.None);

            await repository.UpdateStatusAsync(createdId, IngestionStatusDto.Completed, errorMessage: null, CancellationToken.None);

            DatasetDto? loaded = await repository.GetByIdAsync(createdId, CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(IngestionStatusDto.Completed);
        }

        [Fact]
        public async Task UpdateItemCountAsync_ActuallyPersistsTheNewCount()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetDto dataset = new()
            {
                Id = Guid.NewGuid(),
                Name = "Count test",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };
            Guid createdId = await repository.CreateAsync(dataset, CancellationToken.None);

            await repository.UpdateItemCountAsync(createdId, 12345, CancellationToken.None);

            DatasetDto? loaded = await repository.GetByIdAsync(createdId, CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.TotalItems.Should().Be(12345);
        }

        [Fact]
        public async Task DeleteAsync_RemovesDataset()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetDto dataset = new()
            {
                Id = Guid.NewGuid(),
                Name = "ToDelete",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
            };

            Guid createdId = await repository.CreateAsync(dataset, CancellationToken.None);

            await repository.DeleteAsync(createdId, CancellationToken.None);
            DatasetDto? loaded = await repository.GetByIdAsync(createdId, CancellationToken.None);

            loaded.Should().BeNull();
        }
    }
}
