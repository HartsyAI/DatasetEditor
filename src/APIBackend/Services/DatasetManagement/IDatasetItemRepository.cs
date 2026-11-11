using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// The item store for local (Parquet-backed) datasets, implemented by the singleton
/// <see cref="DataAccess.Parquet.ParquetItemRepository"/>. This is the only item repository
/// interface: Core used to declare a second, same-named one (insert and count) that ingestion
/// reached through a scoped pass-through adapter, so the same store had two interfaces and two
/// lifetimes (audit K-4).
/// </summary>
public interface IDatasetItemRepository
{
    /// <summary>Appends items to the dataset as one new segment.</summary>
    Task AddRangeAsync(Guid datasetId, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>Gets the dataset's total item count (the manifest's row total, not a scan).</summary>
    Task<long> GetCountAsync(Guid datasetId, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of items, optionally filtered, starting at <paramref name="cursor"/>.</summary>
    Task<(IReadOnlyList<DatasetItemDto> Items, string? NextCursor)> GetPageAsync(
        Guid datasetId,
        FilterRequest? filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an item within a known dataset (an id-index lookup in that dataset only).</summary>
    Task<DatasetItemDto?> GetItemAsync(Guid datasetId, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Gets an item by id alone, searching every dataset's id index.</summary>
    Task<DatasetItemDto?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Replaces whole items (each matched by id within its dataset).</summary>
    Task UpdateItemAsync(DatasetItemDto item, CancellationToken cancellationToken = default);

    /// <summary>Replaces whole items (each matched by id within its dataset).</summary>
    Task UpdateItemsAsync(IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="edit"/> to the item as it stands, holding the dataset's lock from the
    /// read through the write, so concurrent edits of different fields of one item both survive
    /// (audit E-4). <paramref name="precondition"/>, when given, is checked against the current item
    /// in the same lock (the <c>If-Match</c> check); when it returns false nothing is written.
    /// </summary>
    Task<ItemEditResult> EditItemAsync(
        Guid datasetId,
        Guid itemId,
        Func<DatasetItemDto, bool>? precondition,
        Func<DatasetItemDto, DatasetItemDto> edit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="edit"/> to each of the dataset's items with these ids as they stand,
    /// under the dataset's lock. Returns the edited items; ids the dataset doesn't have are absent.
    /// </summary>
    Task<IReadOnlyList<DatasetItemDto>> EditItemsAsync(
        Guid datasetId,
        IReadOnlyCollection<Guid> itemIds,
        Func<DatasetItemDto, DatasetItemDto> edit,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every item of the dataset (its manifest and segment files).</summary>
    Task DeleteByDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
