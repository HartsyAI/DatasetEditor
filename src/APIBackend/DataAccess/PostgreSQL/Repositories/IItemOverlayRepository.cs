using DatasetStudio.APIBackend.Services.DatasetManagement;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// Stores the edits of streamed (HuggingFace, S3) items, which can't be written back to their
/// source (audit E-3).
/// </summary>
public interface IItemOverlayRepository
{
    /// <summary>The overlays of these items, in one query; items never edited are absent.</summary>
    Task<IReadOnlyDictionary<Guid, ItemOverlay>> GetAsync(Guid datasetId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="edit"/> to one item's overlay as it stands (<see cref="ItemOverlay.Empty"/>
    /// if never edited), with the row locked from the read through the write. When
    /// <paramref name="precondition"/> refuses the current overlay nothing is written and the
    /// result is PreconditionFailed with the current overlay.
    /// </summary>
    Task<(ItemEditStatus Status, ItemOverlay Overlay)> EditAsync(
        Guid datasetId,
        Guid itemId,
        Func<ItemOverlay, bool>? precondition,
        Func<ItemOverlay, ItemOverlay> edit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="edit"/> to each item's overlay as it stands, in one transaction.
    /// An edit that returns null leaves that item unchanged. Returns the overlays written.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ItemOverlay>> EditManyAsync(
        Guid datasetId,
        IReadOnlyCollection<Guid> itemIds,
        Func<Guid, ItemOverlay, ItemOverlay?> edit,
        CancellationToken cancellationToken = default);
}
