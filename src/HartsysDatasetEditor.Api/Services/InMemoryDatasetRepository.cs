using System.Collections.Concurrent;
using HartsysDatasetEditor.Api.Models;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>
/// In-memory dataset repository for local development and smoke testing.
/// TODO: Replace with persistent store implementation (see docs/architecture.md).
/// </summary>
internal sealed class InMemoryDatasetRepository : IDatasetRepository
{
    private readonly ConcurrentDictionary<Guid, DatasetEntity> _datasets = new();

    public Task<DatasetEntity> CreateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
    {
        dataset.CreatedAt = DateTime.UtcNow;
        dataset.UpdatedAt = dataset.CreatedAt;
        _datasets[dataset.Id] = dataset;
        return Task.FromResult(dataset);
    }

    public Task<DatasetEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _datasets.TryGetValue(id, out var dataset);
        return Task.FromResult(dataset);
    }

    public Task<IReadOnlyList<DatasetEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DatasetEntity> results = _datasets.Values
            .OrderByDescending(d => d.CreatedAt)
            .ToList();
        return Task.FromResult(results);
    }

    public Task UpdateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default)
    {
        dataset.UpdatedAt = DateTime.UtcNow;
        _datasets[dataset.Id] = dataset;
        return Task.CompletedTask;
    }
}
