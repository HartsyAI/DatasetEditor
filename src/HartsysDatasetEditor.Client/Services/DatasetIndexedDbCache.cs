using HartsysDatasetEditor.Contracts.Datasets;
using Microsoft.Extensions.Logging;

namespace HartsysDatasetEditor.Client.Services;

/// <summary>
/// Placeholder IndexedDB cache for dataset pages. TODO: implement actual JS interop-backed persistence (docs/architecture.md §3.1).
/// </summary>
public sealed class DatasetIndexedDbCache
{
    private readonly ILogger<DatasetIndexedDbCache> _logger;

    public DatasetIndexedDbCache(ILogger<DatasetIndexedDbCache> logger)
    {
        _logger = logger;
    }

    public Task SavePageAsync(Guid datasetId, string? cursor, IReadOnlyList<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("IndexedDB cache stub: saving {Count} items for dataset {DatasetId} (cursor={Cursor})", items.Count, datasetId, cursor);
        // TODO: Persist page payload into IndexedDB via IJSRuntime once storage schema is finalized.
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DatasetItemDto>?> TryLoadPageAsync(Guid datasetId, string? cursor, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("IndexedDB cache stub: lookup for dataset {DatasetId} (cursor={Cursor})", datasetId, cursor);
        // TODO: Retrieve cached payload from IndexedDB and deserialize into DTOs.
        return Task.FromResult<IReadOnlyList<DatasetItemDto>?>(null);
    }

    public Task ClearAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("IndexedDB cache stub: clearing cached entries for dataset {DatasetId}", datasetId);
        // TODO: Remove dataset-specific entries from IndexedDB once implemented.
        return Task.CompletedTask;
    }
}
