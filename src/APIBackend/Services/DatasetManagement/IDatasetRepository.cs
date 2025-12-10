using DatasetStudio.APIBackend.Models;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

public interface IDatasetRepository
{
    Task<DatasetEntity> CreateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default);
    Task<DatasetEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatasetEntity>> ListAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(DatasetEntity dataset, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

