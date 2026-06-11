using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class DatasetRepositoryTests
    {
        private static DatasetStudioDbContext CreateInMemoryContext()
        {
            DbContextOptionsBuilder<DatasetStudioDbContext> builder = new DbContextOptionsBuilder<DatasetStudioDbContext>();
            builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            DatasetStudioDbContext context = new DatasetStudioDbContext(builder.Options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task CreateAndGetAsync_PersistsDataset()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetEntity entity = new DatasetEntity
            {
                Name = "Test dataset",
                Description = "Description",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            DatasetEntity created = await repository.CreateAsync(entity, CancellationToken.None);
            DatasetEntity? loaded = await repository.GetAsync(created.Id, CancellationToken.None);

            loaded.Should().NotBeNull();
            if (loaded != null)
            {
                loaded.Name.Should().Be("Test dataset");
                loaded.Description.Should().Be("Description");
            }
        }

        [Fact]
        public async Task ListAsync_ReturnsDatasetsOrderedByCreatedAtDescending()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetEntity older = new DatasetEntity
            {
                Name = "Older",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
            };

            DatasetEntity newer = new DatasetEntity
            {
                Name = "Newer",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await repository.CreateAsync(older, CancellationToken.None);
            await repository.CreateAsync(newer, CancellationToken.None);

            IReadOnlyList<DatasetEntity> list = await repository.ListAsync(CancellationToken.None);

            list.Count.Should().Be(2);
            list[0].Name.Should().Be("Newer");
            list[1].Name.Should().Be("Older");
        }

        [Fact]
        public async Task DeleteAsync_RemovesDataset()
        {
            using DatasetStudioDbContext context = CreateInMemoryContext();
            DatasetRepository repository = new DatasetRepository(context);

            DatasetEntity entity = new DatasetEntity
            {
                Name = "ToDelete",
                Format = "CSV",
                Modality = "Image",
                Status = IngestionStatusDto.Pending,
                TotalItems = 0,
                SourceType = DatasetSourceType.LocalUpload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            DatasetEntity created = await repository.CreateAsync(entity, CancellationToken.None);

            await repository.DeleteAsync(created.Id, CancellationToken.None);
            DatasetEntity? loaded = await repository.GetAsync(created.Id, CancellationToken.None);

            loaded.Should().BeNull();
        }
    }
}
