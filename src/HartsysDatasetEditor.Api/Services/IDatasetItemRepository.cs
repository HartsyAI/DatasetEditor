using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Services;

internal interface IDatasetItemRepository
{
    Task AddRangeAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(
        Guid datasetId,
        FilterRequest? filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}
