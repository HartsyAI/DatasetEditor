using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System.Text.Json;

namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Production-ready service for managing dataset items in Parquet format.
/// Provides high-performance columnar storage with full CRUD operations.
/// </summary>
public class ParquetDataService : IParquetDataService
{
    private static readonly ParquetSchema Schema = new ParquetSchema(
        new DataField<Guid>("Id"),
        new DataField<Guid>("DatasetId"),
        new DataField<string>("ExternalId"),
        new DataField<string>("Title"),
        new DataField<string?>("Description"),
        new DataField<string?>("ThumbnailUrl"),
        new DataField<string?>("ImageUrl"),
        new DataField<int>("Width"),
        new DataField<int>("Height"),
        new DataField<string>("TagsJson"),           // JSON array
        new DataField<bool>("IsFavorite"),
        new DataField<string>("MetadataJson"),        // JSON object
        new DataField<DateTime>("CreatedAt"),
        new DataField<DateTime>("UpdatedAt")
    );

    /// <inheritdoc/>
    public async Task WriteAsync(string filePath, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists(filePath);

            var itemList = items.ToList();
            if (itemList.Count == 0)
            {
                Logs.Warning($"[ParquetDataService] Attempted to write 0 items to {filePath}");
                return;
            }

            using var stream = File.Create(filePath);
            using var writer = await ParquetWriter.CreateAsync(Schema, stream, cancellationToken: cancellationToken);

            // Write in a single row group for simplicity
            using var rowGroup = writer.CreateRowGroup();

            var ids = new List<Guid>();
            var datasetIds = new List<Guid>();
            var externalIds = new List<string>();
            var titles = new List<string>();
            var descriptions = new List<string?>();
            var thumbnailUrls = new List<string?>();
            var imageUrls = new List<string?>();
            var widths = new List<int>();
            var heights = new List<int>();
            var tagsJson = new List<string>();
            var isFavorites = new List<bool>();
            var metadataJson = new List<string>();
            var createdAts = new List<DateTime>();
            var updatedAts = new List<DateTime>();

            foreach (var item in itemList)
            {
                ids.Add(item.Id);
                datasetIds.Add(item.DatasetId);
                externalIds.Add(item.ExternalId);
                titles.Add(item.Title);
                descriptions.Add(item.Description);
                thumbnailUrls.Add(item.ThumbnailUrl);
                imageUrls.Add(item.ImageUrl);
                widths.Add(item.Width);
                heights.Add(item.Height);
                tagsJson.Add(JsonSerializer.Serialize(item.Tags));
                isFavorites.Add(item.IsFavorite);
                metadataJson.Add(JsonSerializer.Serialize(item.Metadata));
                createdAts.Add(item.CreatedAt);
                updatedAts.Add(item.UpdatedAt);
            }

            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[0], ids.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[1], datasetIds.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[2], externalIds.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[3], titles.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[4], descriptions.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[5], thumbnailUrls.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[6], imageUrls.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[7], widths.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[8], heights.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[9], tagsJson.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[10], isFavorites.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[11], metadataJson.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[12], createdAts.ToArray()), cancellationToken);
            await rowGroup.WriteColumnAsync(new DataColumn(Schema.DataFields[13], updatedAts.ToArray()), cancellationToken);

            Logs.Info($"[ParquetDataService] Wrote {itemList.Count} items to {filePath}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to write to {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AppendAsync(string filePath, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parquet doesn't support true append mode - need to read existing, combine, and rewrite
            var existing = await ReadAllItemsAsync(filePath, cancellationToken);
            var combined = existing.Concat(items);
            await WriteAsync(filePath, combined, cancellationToken);

            Logs.Info($"[ParquetDataService] Appended {items.Count()} items to {filePath}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to append to {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<DatasetItemDto>> ReadAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new PagedResult<DatasetItemDto> { Items = new List<DatasetItemDto>(), TotalCount = 0 };
            }

            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var totalCount = allItems.Count;
            var pagedItems = allItems.Skip(offset).Take(limit).ToList();

            return new PagedResult<DatasetItemDto>
            {
                Items = pagedItems,
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to read from {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return 0;
            }

            using var stream = File.OpenRead(filePath);
            using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);

            long count = 0;
            for (int i = 0; i < reader.RowGroupCount; i++)
            {
                using var rowGroup = reader.OpenRowGroupReader(i);
                count += rowGroup.RowCount;
            }

            return count;
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to get count from {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<DatasetItemDto?> ReadItemAsync(string filePath, string itemId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            return allItems.FirstOrDefault(i => i.ExternalId == itemId);
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to read item {itemId} from {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateItemAsync(string filePath, DatasetItemDto item, CancellationToken cancellationToken = default)
    {
        try
        {
            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var updatedItems = allItems.Select(i => i.ExternalId == item.ExternalId ? item : i).ToList();
            await WriteAsync(filePath, updatedItems, cancellationToken);

            Logs.Info($"[ParquetDataService] Updated item {item.ExternalId} in {filePath}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to update item in {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteItemAsync(string filePath, string itemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var filteredItems = allItems.Where(i => i.ExternalId != itemId).ToList();
            await WriteAsync(filePath, filteredItems, cancellationToken);

            Logs.Info($"[ParquetDataService] Deleted item {itemId} from {filePath}");
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to delete item from {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<DatasetItemDto>> SearchAsync(string filePath, string query, int offset, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new PagedResult<DatasetItemDto> { Items = new List<DatasetItemDto>(), TotalCount = 0 };
            }

            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var searchLower = query.ToLowerInvariant();

            var filtered = allItems.Where(i =>
                i.Title.ToLowerInvariant().Contains(searchLower) ||
                (i.Description?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                i.Tags.Any(t => t.ToLowerInvariant().Contains(searchLower))
            ).ToList();

            var totalCount = filtered.Count;
            var pagedItems = filtered.Skip(offset).Take(limit).ToList();

            return new PagedResult<DatasetItemDto>
            {
                Items = pagedItems,
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to search in {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<DatasetItemDto>> GetByTagAsync(string filePath, string tag, int offset, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new PagedResult<DatasetItemDto> { Items = new List<DatasetItemDto>(), TotalCount = 0 };
            }

            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var filtered = allItems.Where(i => i.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();

            var totalCount = filtered.Count;
            var pagedItems = filtered.Skip(offset).Take(limit).ToList();

            return new PagedResult<DatasetItemDto>
            {
                Items = pagedItems,
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to filter by tag in {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<DatasetItemDto>> GetFavoritesAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new PagedResult<DatasetItemDto> { Items = new List<DatasetItemDto>(), TotalCount = 0 };
            }

            var allItems = await ReadAllItemsAsync(filePath, cancellationToken);
            var filtered = allItems.Where(i => i.IsFavorite).ToList();

            var totalCount = filtered.Count;
            var pagedItems = filtered.Skip(offset).Take(limit).ToList();

            return new PagedResult<DatasetItemDto>
            {
                Items = pagedItems,
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            Logs.Error($"[ParquetDataService] Failed to get favorites from {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public bool Exists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <inheritdoc/>
    public void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Logs.Info($"[ParquetDataService] Deleted {filePath}");
        }
    }

    /// <summary>
    /// Reads all items from a Parquet file (internal helper)
    /// </summary>
    private async Task<List<DatasetItemDto>> ReadAllItemsAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new List<DatasetItemDto>();
        }

        var items = new List<DatasetItemDto>();

        using var stream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);

        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            int rowCount = (int)rowGroup.RowCount;

            var ids = (await rowGroup.ReadColumnAsync(Schema.DataFields[0], cancellationToken)).Data.Cast<Guid>().ToArray();
            var datasetIds = (await rowGroup.ReadColumnAsync(Schema.DataFields[1], cancellationToken)).Data.Cast<Guid>().ToArray();
            var externalIds = (await rowGroup.ReadColumnAsync(Schema.DataFields[2], cancellationToken)).Data.Cast<string>().ToArray();
            var titles = (await rowGroup.ReadColumnAsync(Schema.DataFields[3], cancellationToken)).Data.Cast<string>().ToArray();
            var descriptions = (await rowGroup.ReadColumnAsync(Schema.DataFields[4], cancellationToken)).Data.Cast<string?>().ToArray();
            var thumbnailUrls = (await rowGroup.ReadColumnAsync(Schema.DataFields[5], cancellationToken)).Data.Cast<string?>().ToArray();
            var imageUrls = (await rowGroup.ReadColumnAsync(Schema.DataFields[6], cancellationToken)).Data.Cast<string?>().ToArray();
            var widths = (await rowGroup.ReadColumnAsync(Schema.DataFields[7], cancellationToken)).Data.Cast<int>().ToArray();
            var heights = (await rowGroup.ReadColumnAsync(Schema.DataFields[8], cancellationToken)).Data.Cast<int>().ToArray();
            var tagsJson = (await rowGroup.ReadColumnAsync(Schema.DataFields[9], cancellationToken)).Data.Cast<string>().ToArray();
            var isFavorites = (await rowGroup.ReadColumnAsync(Schema.DataFields[10], cancellationToken)).Data.Cast<bool>().ToArray();
            var metadataJson = (await rowGroup.ReadColumnAsync(Schema.DataFields[11], cancellationToken)).Data.Cast<string>().ToArray();
            var createdAts = (await rowGroup.ReadColumnAsync(Schema.DataFields[12], cancellationToken)).Data.Cast<DateTime>().ToArray();
            var updatedAts = (await rowGroup.ReadColumnAsync(Schema.DataFields[13], cancellationToken)).Data.Cast<DateTime>().ToArray();

            for (int j = 0; j < rowCount; j++)
            {
                var item = new DatasetItemDto
                {
                    Id = ids[j],
                    DatasetId = datasetIds[j],
                    ExternalId = externalIds[j],
                    Title = titles[j],
                    Description = descriptions[j],
                    ThumbnailUrl = thumbnailUrls[j],
                    ImageUrl = imageUrls[j],
                    Width = widths[j],
                    Height = heights[j],
                    Tags = JsonSerializer.Deserialize<List<string>>(tagsJson[j]) ?? new List<string>(),
                    IsFavorite = isFavorites[j],
                    Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson[j]) ?? new Dictionary<string, string>(),
                    CreatedAt = createdAts[j],
                    UpdatedAt = updatedAts[j]
                };

                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Ensures the directory for a file path exists
    /// </summary>
    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
