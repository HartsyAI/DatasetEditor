using HartsysDatasetEditor.Api.Models;

namespace HartsysDatasetEditor.Api.Services;

internal interface IDatasetRepository
{
    Task<DatasetEntity> CreateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default);
    Task<DatasetEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatasetEntity>> ListAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default);
}
