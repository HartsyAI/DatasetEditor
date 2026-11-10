using System.Text.Json;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// EF Core / PostgreSQL store for streamed-item overlays (table <c>item_overlays</c>).
/// </summary>
/// <remarks>
/// An edit runs in one transaction: it first inserts an empty row (version 0) for every item that
/// has none, then locks the rows with <c>SELECT … FOR UPDATE</c>, applies the edit to the values it
/// read and writes them back with the version incremented. Inserting first means there is always
/// a row to lock, so two first edits of one item serialise like any other two edits instead of
/// both reading "no overlay" and the second overwriting the first. Rows are inserted and locked in
/// id order, so two bulk edits over overlapping items can't deadlock.
/// </remarks>
public sealed class ItemOverlayRepository : IItemOverlayRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly DatasetStudioDbContext _db;

    /// <summary>Creates the repository over the request's context.</summary>
    public ItemOverlayRepository(DatasetStudioDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ItemOverlay>> GetAsync(Guid datasetId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, ItemOverlay>();
        }

        List<Guid> ids = itemIds.Distinct().ToList();
        List<ItemOverlayEntity> rows = await _db.ItemOverlays
            .AsNoTracking()
            .Where(o => o.DatasetId == datasetId && ids.Contains(o.ItemId) && o.Version > 0)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.ItemId, ToOverlay);
    }

    /// <inheritdoc />
    public async Task<(ItemEditStatus Status, ItemOverlay Overlay)> EditAsync(
        Guid datasetId,
        Guid itemId,
        Func<ItemOverlay, bool>? precondition,
        Func<ItemOverlay, ItemOverlay> edit,
        CancellationToken cancellationToken = default)
    {
        ItemOverlay? refused = null;
        IReadOnlyDictionary<Guid, ItemOverlay> written = await EditCoreAsync(
            datasetId,
            new[] { itemId },
            (_, current) =>
            {
                if (precondition is not null && !precondition(current))
                {
                    refused = current;
                    return null;
                }
                return edit(current);
            },
            rollBackOnRefusal: true,
            cancellationToken);

        return refused is not null
            ? (ItemEditStatus.PreconditionFailed, refused)
            : (ItemEditStatus.Updated, written[itemId]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, ItemOverlay>> EditManyAsync(
        Guid datasetId,
        IReadOnlyCollection<Guid> itemIds,
        Func<Guid, ItemOverlay, ItemOverlay?> edit,
        CancellationToken cancellationToken = default)
    {
        return EditCoreAsync(datasetId, itemIds, edit, rollBackOnRefusal: false, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, ItemOverlay>> EditCoreAsync(
        Guid datasetId,
        IReadOnlyCollection<Guid> itemIds,
        Func<Guid, ItemOverlay, ItemOverlay?> edit,
        bool rollBackOnRefusal,
        CancellationToken cancellationToken)
    {
        Guid[] ids = itemIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, ItemOverlay>();
        }

        // The context retries on transient failures, which needs a user transaction wrapped in
        // the execution strategy; each attempt starts from a clean change tracker.
        IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            DateTime now = DateTime.UtcNow;
            await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            await _db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO item_overlays (dataset_id, item_id, version, updated_at)
                SELECT @dataset, id, 0, @now FROM unnest(@ids) AS id ORDER BY id
                ON CONFLICT (dataset_id, item_id) DO NOTHING
                """,
                new object[]
                {
                    new NpgsqlParameter("dataset", NpgsqlDbType.Uuid) { Value = datasetId },
                    new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now },
                    new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids },
                },
                cancellationToken);

            List<ItemOverlayEntity> rows = await _db.ItemOverlays
                .FromSqlRaw(
                    "SELECT * FROM item_overlays WHERE dataset_id = @dataset AND item_id = ANY(@ids) ORDER BY item_id FOR UPDATE",
                    new NpgsqlParameter("dataset", NpgsqlDbType.Uuid) { Value = datasetId },
                    new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids })
                .AsTracking()
                .ToListAsync(cancellationToken);

            Dictionary<Guid, ItemOverlay> written = new();
            foreach (ItemOverlayEntity row in rows)
            {
                ItemOverlay current = ToOverlay(row);
                ItemOverlay? next = edit(row.ItemId, current);
                if (next is null)
                {
                    if (rollBackOnRefusal)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return (IReadOnlyDictionary<Guid, ItemOverlay>)new Dictionary<Guid, ItemOverlay>();
                    }
                    continue;
                }

                row.Title = next.Title;
                row.Description = next.Description;
                row.TagsJson = next.Tags is null ? null : JsonSerializer.Serialize(next.Tags, JsonOptions);
                row.IsFavorite = next.IsFavorite;
                row.MetadataJson = next.Metadata is null ? null : JsonSerializer.Serialize(next.Metadata, JsonOptions);
                row.Version = current.Version + 1;
                row.UpdatedAt = now;
                written[row.ItemId] = next with { Version = row.Version };
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return written;
        });
    }

    private static ItemOverlay ToOverlay(ItemOverlayEntity row)
    {
        return new ItemOverlay
        {
            Title = row.Title,
            Description = row.Description,
            Tags = row.TagsJson is null ? null : JsonSerializer.Deserialize<List<string>>(row.TagsJson, JsonOptions),
            IsFavorite = row.IsFavorite,
            Metadata = row.MetadataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.MetadataJson, JsonOptions),
            Version = row.Version
        };
    }
}
