using System.Text.Json;
using DatasetStudio.DTO.Datasets;
using Parquet;
using Parquet.Data;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Writes dataset items to Parquet files with automatic sharding and batch optimization.
/// Handles writing billions of items by splitting them across multiple shard files.
/// </summary>
public class ParquetItemWriter : IDisposable
{
    private readonly string _dataDirectory;
    private readonly Dictionary<int, ShardWriter> _activeWriters = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ParquetItemWriter.
    /// </summary>
    /// <param name="dataDirectory">Directory where Parquet files will be stored.</param>
    public ParquetItemWriter(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
        Directory.CreateDirectory(_dataDirectory);
    }

    /// <summary>
    /// Writes a batch of items to Parquet files, automatically sharding as needed.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="items">Items to write.</param>
    /// <param name="startIndex">Starting index for determining shard placement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteBatchAsync(
        Guid datasetId,
        IEnumerable<DatasetItemDto> items,
        long startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        // Group items by shard
        var itemsByShard = new Dictionary<int, List<DatasetItemDto>>();
        long currentIndex = startIndex;

        foreach (var item in itemList)
        {
            int shardIndex = ParquetSchemaDefinition.GetShardIndex(currentIndex);

            if (!itemsByShard.ContainsKey(shardIndex))
                itemsByShard[shardIndex] = new List<DatasetItemDto>();

            itemsByShard[shardIndex].Add(item);
            currentIndex++;
        }

        // Write to each shard
        foreach (var (shardIndex, shardItems) in itemsByShard)
        {
            await WriteToShardAsync(datasetId, shardIndex, shardItems, cancellationToken);
        }
    }

    /// <summary>
    /// Writes items to a specific shard file.
    /// </summary>
    private async Task WriteToShardAsync(
        Guid datasetId,
        int shardIndex,
        List<DatasetItemDto> items,
        CancellationToken cancellationToken)
    {
        var fileName = ParquetSchemaDefinition.GetShardFileName(datasetId, shardIndex);
        var filePath = Path.Combine(_dataDirectory, fileName);

        // Convert items to columnar format
        var columns = ConvertToColumns(items);

        // Append to existing file or create new one
        if (File.Exists(filePath))
        {
            await AppendToFileAsync(filePath, columns, cancellationToken);
        }
        else
        {
            await CreateFileAsync(filePath, columns, cancellationToken);
        }
    }

    /// <summary>
    /// Creates a new Parquet file with the given data.
    /// </summary>
    private static async Task CreateFileAsync(
        string filePath,
        Dictionary<string, Array> columns,
        CancellationToken cancellationToken)
    {
        using var stream = File.Create(filePath);
        using var writer = await ParquetWriter.CreateAsync(
            ParquetSchemaDefinition.Schema,
            stream,
            ParquetSchemaDefinition.WriterOptions,
            cancellationToken: cancellationToken);

        using var groupWriter = writer.CreateRowGroup();
        await WriteColumnsAsync(groupWriter, columns, cancellationToken);
    }

    /// <summary>
    /// Appends data to an existing Parquet file.
    /// </summary>
    private static async Task AppendToFileAsync(
        string filePath,
        Dictionary<string, Array> columns,
        CancellationToken cancellationToken)
    {
        using var stream = File.Open(filePath, FileMode.Append, FileAccess.Write);
        using var writer = await ParquetWriter.CreateAsync(
            ParquetSchemaDefinition.Schema,
            stream,
            ParquetSchemaDefinition.WriterOptions,
            append: true,
            cancellationToken: cancellationToken);

        using var groupWriter = writer.CreateRowGroup();
        await WriteColumnsAsync(groupWriter, columns, cancellationToken);
    }

    /// <summary>
    /// Writes column data to a row group.
    /// </summary>
    private static async Task WriteColumnsAsync(
        ParquetRowGroupWriter groupWriter,
        Dictionary<string, Array> columns,
        CancellationToken cancellationToken)
    {
        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[0],
            (Guid[])columns["id"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[1],
            (Guid[])columns["dataset_id"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[2],
            (string[])columns["external_id"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[3],
            (string[])columns["title"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[4],
            (string[])columns["description"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[5],
            (string[])columns["image_url"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[6],
            (string[])columns["thumbnail_url"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[7],
            (int[])columns["width"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[8],
            (int[])columns["height"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[9],
            (double[])columns["aspect_ratio"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[10],
            (string[])columns["tags_json"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[11],
            (bool[])columns["is_favorite"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[12],
            (string[])columns["metadata_json"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[13],
            (DateTime[])columns["created_at"]), cancellationToken);

        await groupWriter.WriteColumnAsync(new DataColumn(
            ParquetSchemaDefinition.Schema.DataFields[14],
            (DateTime[])columns["updated_at"]), cancellationToken);
    }

    /// <summary>
    /// Converts a list of items to columnar arrays for Parquet writing.
    /// </summary>
    private static Dictionary<string, Array> ConvertToColumns(List<DatasetItemDto> items)
    {
        int count = items.Count;

        var ids = new Guid[count];
        var datasetIds = new Guid[count];
        var externalIds = new string[count];
        var titles = new string[count];
        var descriptions = new string[count];
        var imageUrls = new string[count];
        var thumbnailUrls = new string[count];
        var widths = new int[count];
        var heights = new int[count];
        var aspectRatios = new double[count];
        var tagsJson = new string[count];
        var isFavorites = new bool[count];
        var metadataJson = new string[count];
        var createdAts = new DateTime[count];
        var updatedAts = new DateTime[count];

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

        for (int i = 0; i < count; i++)
        {
            var item = items[i];

            ids[i] = item.Id;
            datasetIds[i] = item.DatasetId;
            externalIds[i] = item.ExternalId ?? string.Empty;
            titles[i] = item.Title ?? string.Empty;
            descriptions[i] = item.Description ?? string.Empty;
            imageUrls[i] = item.ImageUrl ?? string.Empty;
            thumbnailUrls[i] = item.ThumbnailUrl ?? string.Empty;
            widths[i] = item.Width;
            heights[i] = item.Height;
            aspectRatios[i] = item.Height > 0 ? (double)item.Width / item.Height : 0.0;
            tagsJson[i] = JsonSerializer.Serialize(item.Tags, jsonOptions);
            isFavorites[i] = item.IsFavorite;
            metadataJson[i] = JsonSerializer.Serialize(item.Metadata, jsonOptions);
            createdAts[i] = item.CreatedAt;
            updatedAts[i] = item.UpdatedAt;
        }

        return new Dictionary<string, Array>
        {
            ["id"] = ids,
            ["dataset_id"] = datasetIds,
            ["external_id"] = externalIds,
            ["title"] = titles,
            ["description"] = descriptions,
            ["image_url"] = imageUrls,
            ["thumbnail_url"] = thumbnailUrls,
            ["width"] = widths,
            ["height"] = heights,
            ["aspect_ratio"] = aspectRatios,
            ["tags_json"] = tagsJson,
            ["is_favorite"] = isFavorites,
            ["metadata_json"] = metadataJson,
            ["created_at"] = createdAts,
            ["updated_at"] = updatedAts
        };
    }

    /// <summary>
    /// Flushes and closes all active writers.
    /// </summary>
    public async Task FlushAsync()
    {
        foreach (var writer in _activeWriters.Values)
        {
            await writer.DisposeAsync();
        }
        _activeWriters.Clear();
    }

    /// <summary>
    /// Deletes all shard files for a specific dataset.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    public void DeleteDatasetShards(Guid datasetId)
    {
        var pattern = $"dataset_{datasetId:N}_shard_*.parquet";
        var files = Directory.GetFiles(_dataDirectory, pattern);

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // File might be in use, ignore
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var writer in _activeWriters.Values)
        {
            writer.Dispose();
        }
        _activeWriters.Clear();

        _disposed = true;
    }

    /// <summary>
    /// Helper class to manage individual shard writers.
    /// </summary>
    private class ShardWriter : IDisposable, IAsyncDisposable
    {
        private readonly FileStream _stream;
        private readonly ParquetWriter _writer;

        public ShardWriter(FileStream stream, ParquetWriter writer)
        {
            _stream = stream;
            _writer = writer;
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _stream?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_writer != null)
                await _writer.DisposeAsync();
            if (_stream != null)
                await _stream.DisposeAsync();
        }
    }
}
