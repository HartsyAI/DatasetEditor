using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

public interface IDatasetItemRepository
{
    Task AddRangeAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(
        Guid datasetId,
        FilterRequest? filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DatasetItemDto?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task UpdateItemAsync(DatasetItemDto item, CancellationToken cancellationToken = default);

    Task UpdateItemsAsync(IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    Task DeleteByDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default);
}

