using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using LiteDB;

namespace DatasetStudio.APIBackend.DataAccess.LiteDB.Repositories;

/// <summary>LiteDB-backed implementation of the API dataset repository.</summary>
internal sealed class DatasetRepository : IDatasetRepository
{
    private const string CollectionName = "api_datasets";
    private readonly ILiteCollection<DatasetEntity> _collection;

    public DatasetRepository(LiteDatabase database)
    {
        if (database is null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        _collection = database.GetCollection<DatasetEntity>(CollectionName);
        _collection.EnsureIndex(x => x.Id);
        _collection.EnsureIndex(x => x.CreatedAt);
        _collection.EnsureIndex(x => x.UpdatedAt);
    }

    public Task<DatasetEntity> CreateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
    {
        dataset.CreatedAt = DateTime.UtcNow;
        dataset.UpdatedAt = dataset.CreatedAt;
        if (dataset.Id == Guid.Empty)
        {
            dataset.Id = Guid.NewGuid();
        }

        _collection.Insert(dataset);
        return Task.FromResult(dataset);
    }

    public Task<DatasetEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DatasetEntity? entity = _collection.FindById(new BsonValue(id));
        return Task.FromResult<DatasetEntity?>(entity);
    }

    public Task<IReadOnlyList<DatasetEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<DatasetEntity> results = _collection.Query()
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<DatasetEntity>>(results);
    }

    public Task UpdateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
    {
        dataset.UpdatedAt = DateTime.UtcNow;
        _collection.Update(dataset);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _collection.Delete(new BsonValue(id));
        return Task.CompletedTask;
    }
}
