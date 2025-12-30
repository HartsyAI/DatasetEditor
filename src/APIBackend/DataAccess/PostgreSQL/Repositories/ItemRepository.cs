using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

public sealed class ItemRepository : Core.Abstractions.Repositories.IDatasetItemRepository
{
    private readonly ParquetItemRepository _parquetRepo;

    public ItemRepository(ParquetItemRepository parquetRepo)
    {
        _parquetRepo = parquetRepo ?? throw new ArgumentNullException(nameof(parquetRepo));
    }

    public async Task InsertItemsAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        await _parquetRepo.AddRangeAsync(datasetId, items, cancellationToken);
    }

    public async Task<PagedResult<DatasetItemDto>> GetItemsAsync(Guid datasetId, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, null, null, offset + limit, cancellationToken);
        var pagedItems = items.Skip(offset).Take(limit).ToList();
        var totalCount = await _parquetRepo.GetCountAsync(datasetId, null, cancellationToken);
        return new PagedResult<DatasetItemDto> { Items = pagedItems, TotalCount = totalCount };
    }

    public async Task<DatasetItemDto?> GetItemAsync(Guid datasetId, string itemId, CancellationToken cancellationToken = default)
    {
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, null, null, int.MaxValue, cancellationToken);
        return items.FirstOrDefault(i => i.ExternalId == itemId);
    }

    public async Task UpdateItemAsync(Guid datasetId, DatasetItemDto item, CancellationToken cancellationToken = default)
    {
        await _parquetRepo.UpdateItemAsync(item, cancellationToken);
    }

    public async Task BulkUpdateItemsAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        await _parquetRepo.UpdateItemsAsync(items, cancellationToken);
    }

    public async Task DeleteItemAsync(Guid datasetId, string itemId, CancellationToken cancellationToken = default)
    {
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, null, null, int.MaxValue, cancellationToken);
        var itemToDelete = items.FirstOrDefault(i => i.ExternalId == itemId);
        if (itemToDelete != null)
        {
            var remaining = items.Where(i => i.ExternalId != itemId);
            await _parquetRepo.DeleteByDatasetAsync(datasetId, cancellationToken);
            await _parquetRepo.AddRangeAsync(datasetId, remaining, cancellationToken);
        }
    }

    public async Task<long> GetItemCountAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return await _parquetRepo.GetCountAsync(datasetId, null, cancellationToken);
    }

    public async Task<PagedResult<DatasetItemDto>> SearchItemsAsync(Guid datasetId, string query, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var filter = new FilterRequest { SearchQuery = query };
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, filter, null, offset + limit, cancellationToken);
        var pagedItems = items.Skip(offset).Take(limit).ToList();
        var totalCount = await _parquetRepo.GetCountAsync(datasetId, filter, cancellationToken);
        return new PagedResult<DatasetItemDto> { Items = pagedItems, TotalCount = totalCount };
    }

    public async Task<PagedResult<DatasetItemDto>> GetItemsByTagAsync(Guid datasetId, string tag, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var filter = new FilterRequest { Tags = new[] { tag } };
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, filter, null, offset + limit, cancellationToken);
        var pagedItems = items.Skip(offset).Take(limit).ToList();
        var totalCount = await _parquetRepo.GetCountAsync(datasetId, filter, cancellationToken);
        return new PagedResult<DatasetItemDto> { Items = pagedItems, TotalCount = totalCount };
    }

    public async Task<PagedResult<DatasetItemDto>> GetFavoriteItemsAsync(Guid datasetId, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var filter = new FilterRequest { FavoritesOnly = true };
        var (items, _) = await _parquetRepo.GetPageAsync(datasetId, filter, null, offset + limit, cancellationToken);
        var pagedItems = items.Skip(offset).Take(limit).ToList();
        var totalCount = await _parquetRepo.GetCountAsync(datasetId, filter, cancellationToken);
        return new PagedResult<DatasetItemDto> { Items = pagedItems, TotalCount = totalCount };
    }
}
