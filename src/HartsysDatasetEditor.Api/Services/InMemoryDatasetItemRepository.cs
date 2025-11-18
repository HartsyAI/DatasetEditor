using System.Collections.Concurrent;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// In-memory dataset item repository used for initial API scaffolding.
/// TODO: Swap with persistent implementation once storage layer lands.
/// </summary>
internal sealed class InMemoryDatasetItemRepository : IDatasetItemRepository
{
    private readonly ConcurrentDictionary<Guid, List<DatasetItemDto>> _items = new();

    public Task AddRangeAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        var itemsList = items.ToList(); // Materialize to count
        var list = _items.GetOrAdd(datasetId, _ => new List<DatasetItemDto>());
        lock (list)
        {
            list.AddRange(itemsList);
            Console.WriteLine($"[InMemoryRepo] Added {itemsList.Count} items to dataset {datasetId}. Total in repo: {list.Count}");
        }
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(
        Guid datasetId,
        FilterRequest? filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var parsedCursor) && parsedCursor >= 0)
        {
            startIndex = parsedCursor;
        }

        if (!_items.TryGetValue(datasetId, out var list) || list.Count == 0)
        {
            return Task.FromResult<(IReadOnlyList<DatasetItemDto>, string?)>(((IReadOnlyList<DatasetItemDto>)Array.Empty<DatasetItemDto>(), null));
        }

        List<DatasetItemDto> snapshot;
        lock (list)
        {
            snapshot = list.OrderByDescending(i => i.Id).ToList();
        }

        // TODO: Apply FilterRequest once implemented (see docs/architecture.md).

        var page = snapshot.Skip(startIndex).Take(pageSize).ToList();
        var nextCursor = startIndex + page.Count < snapshot.Count
            ? (startIndex + page.Count).ToString()
            : null;

        return Task.FromResult<(IReadOnlyList<DatasetItemDto>, string?)>(((IReadOnlyList<DatasetItemDto>)page, nextCursor));
    }
}
