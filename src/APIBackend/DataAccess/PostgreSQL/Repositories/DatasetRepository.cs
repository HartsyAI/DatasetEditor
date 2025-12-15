using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using Microsoft.EntityFrameworkCore;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories
{
    /// <summary>
    /// Entity Framework Core implementation of IDatasetRepository for PostgreSQL.
    /// </summary>
    public sealed class DatasetRepository : IDatasetRepository
    {
        private readonly DatasetStudioDbContext _dbContext;

        public DatasetRepository(DatasetStudioDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<DatasetEntity> CreateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            _dbContext.Datasets.Add(dataset);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return dataset;
        }

        public async Task<DatasetEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            DatasetEntity? entity = await _dbContext.Datasets
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            return entity;
        }

        public async Task<IReadOnlyList<DatasetEntity>> ListAsync(CancellationToken cancellationToken = default)
        {
            List<DatasetEntity> datasets = await _dbContext.Datasets
                .AsNoTracking()
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken);

            return datasets;
        }

        public async Task UpdateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            _dbContext.Datasets.Update(dataset);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            DatasetEntity? existing = await _dbContext.Datasets
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (existing == null)
            {
                return;
            }

            _dbContext.Datasets.Remove(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
