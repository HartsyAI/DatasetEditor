using System.Text.Json;
using DatasetStudio.DTO.Datasets;
using Parquet;
using Parquet.Data;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Writes immutable segment files. A segment is written whole to a temp file beside its final
/// name and renamed into place, so a file under a segment name is always complete. Appending to
/// a shard in place used to rewrite its footer, and a process killed mid-append left the whole
/// shard unreadable (audit P-11).
/// </summary>
public class ParquetItemWriter : IDisposable
{
    private readonly string _dataDirectory;

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
    /// Writes <paramref name="items"/> as a new segment, in row groups of
    /// <see cref="ParquetSchemaDefinition.DefaultBatchSize"/>. Returns the ids in row order.
    /// </summary>
    public Task<List<Guid>> WriteSegmentAsync(string path, IReadOnlyList<DatasetItemDto> items, CancellationToken cancellationToken)
    {
        return WriteSegmentAsync(path, Batches(items), cancellationToken);
    }

    /// <summary>
    /// Writes a segment from row groups produced one at a time (a rewrite or a merge streams
    /// them, holding one group in memory). Nothing appears under <paramref name="path"/> until the
    /// whole file is written; on failure or cancellation the temp file is removed and any existing
    /// file at <paramref name="path"/> is untouched. Returns the ids in row order.
    /// </summary>
    public async Task<List<Guid>> WriteSegmentAsync(string path, IAsyncEnumerable<List<DatasetItemDto>> groups, CancellationToken cancellationToken)
    {
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        List<Guid> ids = new();
        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                using (ParquetWriter writer = await ParquetWriter.CreateAsync(
                    ParquetSchemaDefinition.Schema, stream, ParquetSchemaDefinition.WriterOptions, cancellationToken: cancellationToken))
                {
                    writer.CompressionMethod = ParquetSchemaDefinition.Compression;
                    await foreach (List<DatasetItemDto> group in groups.WithCancellation(cancellationToken))
                    {
                        if (group.Count == 0)
                        {
                            continue;
                        }
                        using ParquetRowGroupWriter groupWriter = writer.CreateRowGroup();
                        await WriteColumnsAsync(groupWriter, ConvertToColumns(group), cancellationToken);
                        ids.AddRange(group.Select(i => i.Id));
                    }
                }
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            // The rename is the commit point; it is not cancellable.
            File.Move(tempPath, path, overwrite: true);
            return ids;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // Swept with the dataset's other orphans on its next write.
            }
        }
    }

    private static async IAsyncEnumerable<List<DatasetItemDto>> Batches(IReadOnlyList<DatasetItemDto> items)
    {
        for (int i = 0; i < items.Count; i += ParquetSchemaDefinition.DefaultBatchSize)
        {
            yield return items.Skip(i).Take(ParquetSchemaDefinition.DefaultBatchSize).ToList();
        }
        await Task.CompletedTask;
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


    /// <summary>Nothing to release: every write opens and closes its own file.</summary>
    public void Dispose()
    {
    }
}
