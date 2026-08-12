using DatasetStudio.APIBackend.DataAccess.Parquet;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// Adapts <see cref="Core.Abstractions.Repositories.IDatasetItemRepository"/> (what
/// DatasetIngestionService depends on) onto the real Parquet-backed item store. Despite
/// living in the "PostgreSQL" folder — a pre-existing naming artifact from before the
/// interface was trimmed down — this does no Postgres I/O; item storage is Parquet-only.
/// </summary>
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

    public async Task<long> GetCountAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return await _parquetRepo.GetCountAsync(datasetId, null, cancellationToken);
    }
}
