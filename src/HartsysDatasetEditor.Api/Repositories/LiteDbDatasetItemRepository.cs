using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;
using LiteDB;

namespace HartsysDatasetEditor.Api.Repositories;

/// <summary>
/// LiteDB implementation of the API-facing dataset item repository that stores DatasetItemDto records.
/// </summary>
internal sealed class LiteDbDatasetItemRepository : IDatasetItemRepository
{
    private const string CollectionName = "api_dataset_items";
    private readonly ILiteCollection<DatasetItemDto> _collection;

    public LiteDbDatasetItemRepository(LiteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _collection = database.GetCollection<DatasetItemDto>(CollectionName);
        _collection.EnsureIndex(x => x.DatasetId);
        _collection.EnsureIndex(x => x.Id);
        _collection.EnsureIndex(x => x.CreatedAt);
        _collection.EnsureIndex(x => x.UpdatedAt);
    }

    public Task AddRangeAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        List<DatasetItemDto> materialized = items
            .Select(item => item with { DatasetId = datasetId })
            .ToList();

        _collection.InsertBulk(materialized);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(Guid datasetId, FilterRequest? filter, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        int startIndex = 0;
        if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out int parsedCursor) && parsedCursor >= 0)
        {
            startIndex = parsedCursor;
        }

        ILiteQueryable<DatasetItemDto> queryable = _collection.Query()
            .Where(i => i.DatasetId == datasetId)
            .OrderByDescending(i => i.CreatedAt);

        // TODO: Apply filter once FilterRequest is implemented for persistent storage.

        List<DatasetItemDto> page = queryable
            .Skip(startIndex)
            .Limit(pageSize)
            .ToList();

        long total = _collection.LongCount(i => i.DatasetId == datasetId);
        string? nextCursor = startIndex + page.Count < total
            ? (startIndex + page.Count).ToString()
            : null;

        return Task.FromResult<(IReadOnlyList<DatasetItemDto>, string?)>(((IReadOnlyList<DatasetItemDto>)page, nextCursor));
    }

    public Task<DatasetItemDto?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        DatasetItemDto? item = _collection.FindById(itemId);
        return Task.FromResult(item);
    }

    public Task UpdateItemAsync(DatasetItemDto item, CancellationToken cancellationToken = default)
    {
        _collection.Update(item);
        return Task.CompletedTask;
    }

    public Task UpdateItemsAsync(IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        List<DatasetItemDto> itemList = items.ToList();
        foreach (DatasetItemDto item in itemList)
        {
            _collection.Update(item);
        }
        return Task.CompletedTask;
    }
}
