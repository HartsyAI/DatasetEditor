using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Centralized Parquet schema definition for dataset items.
/// Defines the structure, types, and compression settings for Parquet files.
/// </summary>
public static class ParquetSchemaDefinition
{
    /// <summary>Maximum number of items per Parquet file shard.</summary>
    public const int ItemsPerShard = 10_000_000; // 10 million items per file

    /// <summary>Default batch size for writing operations.</summary>
    public const int DefaultBatchSize = 10_000;

    /// <summary>Compression method used for Parquet files (Snappy provides good balance of speed/compression).</summary>
    public const CompressionMethod Compression = CompressionMethod.Snappy;

    /// <summary>
    /// The Parquet schema for dataset items.
    /// Column order optimized for query performance.
    /// </summary>
    public static readonly ParquetSchema Schema = new(
        // Primary identifiers
        new DataField<Guid>("id"),
        new DataField<Guid>("dataset_id"),

        // External reference
        new DataField<string>("external_id"),

        // Content metadata
        new DataField<string>("title"),
        new DataField<string>("description"),

        // URLs
        new DataField<string>("image_url"),
        new DataField<string>("thumbnail_url"),

        // Dimensions
        new DataField<int>("width"),
        new DataField<int>("height"),

        // Computed field for filtering
        new DataField<double>("aspect_ratio"),

        // Tags as JSON array
        new DataField<string>("tags_json"),

        // Boolean flags
        new DataField<bool>("is_favorite"),

        // Metadata as JSON string
        new DataField<string>("metadata_json"),

        // Timestamps for filtering and sorting
        new DataField<DateTime>("created_at"),
        new DataField<DateTime>("updated_at")
    );

    /// <summary>
    /// Gets the file name for a specific dataset shard.
    /// </summary>
    /// <param name="datasetId">The dataset ID.</param>
    /// <param name="shardIndex">The zero-based shard index.</param>
    /// <returns>The shard file name.</returns>
    public static string GetShardFileName(Guid datasetId, int shardIndex)
    {
        return $"dataset_{datasetId:N}_shard_{shardIndex:D6}.parquet";
    }

    /// <summary>
    /// Calculates which shard a given item index belongs to.
    /// </summary>
    /// <param name="itemIndex">The zero-based item index.</param>
    /// <returns>The shard index.</returns>
    public static int GetShardIndex(long itemIndex)
    {
        return (int)(itemIndex / ItemsPerShard);
    }

    /// <summary>
    /// Calculates the item's index within its shard.
    /// </summary>
    /// <param name="itemIndex">The zero-based global item index.</param>
    /// <returns>The index within the shard.</returns>
    public static int GetIndexWithinShard(long itemIndex)
    {
        return (int)(itemIndex % ItemsPerShard);
    }

    /// <summary>
    /// Parses dataset ID and shard index from a file name.
    /// </summary>
    /// <param name="fileName">The file name (without path).</param>
    /// <param name="datasetId">Output dataset ID.</param>
    /// <param name="shardIndex">Output shard index.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseFileName(string fileName, out Guid datasetId, out int shardIndex)
    {
        datasetId = Guid.Empty;
        shardIndex = -1;

        if (!fileName.StartsWith("dataset_") || !fileName.EndsWith(".parquet"))
            return false;

        try
        {
            // Format: dataset_{guid}_shard_{index}.parquet
            var parts = fileName.Replace("dataset_", "").Replace(".parquet", "").Split("_shard_");
            if (parts.Length != 2)
                return false;

            datasetId = Guid.Parse(parts[0]);
            shardIndex = int.Parse(parts[1]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writer options with optimized settings for dataset items.
    /// </summary>
    public static ParquetOptions WriterOptions => new()
    {
        // TODO: Update to new Parquet.NET API
        // CompressionMethod = Compression,
        // WriteStatistics = true,

        // Enable dictionary encoding for string columns
        UseDictionaryEncoding = true
    };

    /// <summary>
    /// Reader options for reading Parquet files.
    /// </summary>
    public static ParquetOptions ReaderOptions => new()
    {
        // Allow reading files with different schemas (forward compatibility)
        TreatByteArrayAsString = true
    };
}
