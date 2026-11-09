using System.Collections.Concurrent;
using System.Text.Json;
using Parquet;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// Loads, caches and atomically replaces dataset manifests (<c>dataset_{id}.manifest.json</c>).
/// Callers hold the dataset's lock in <see cref="ParquetItemRepository"/>, so one dataset's
/// manifest is never written concurrently.
/// </summary>
public sealed class ParquetManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dataDirectory;
    private readonly ConcurrentDictionary<Guid, ParquetManifest> _cache = new();

    /// <summary>Creates the store over the Parquet directory.</summary>
    public ParquetManifestStore(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>The manifest file name for a dataset.</summary>
    public static string ManifestFileName(Guid datasetId) => $"dataset_{datasetId:N}.manifest.json";

    /// <summary>The file name of a dataset's segment number <paramref name="sequence"/>.</summary>
    public static string SegmentFileName(Guid datasetId, long sequence) => $"dataset_{datasetId:N}_seg_{sequence:D8}.parquet";

    /// <summary>Full path of a file in the Parquet directory.</summary>
    public string PathOf(string fileName) => Path.Combine(_dataDirectory, fileName);

    /// <summary>
    /// The dataset's manifest. A dataset written before manifests existed (shard files only) is
    /// adopted once: its shards become segments in shard order, read from their footers, and the
    /// manifest is saved, without rewriting any data.
    /// </summary>
    public async Task<ParquetManifest> LoadAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(datasetId, out ParquetManifest? cached))
        {
            return cached;
        }

        string manifestPath = PathOf(ManifestFileName(datasetId));
        ParquetManifest manifest;
        if (File.Exists(manifestPath))
        {
            await using FileStream stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ParquetManifest>(stream, JsonOptions, cancellationToken)
                ?? ParquetManifest.Empty;
        }
        else
        {
            manifest = await AdoptLegacyShardsAsync(datasetId, cancellationToken);
            if (manifest.Segments.Count > 0)
            {
                await SaveAsync(datasetId, manifest, cancellationToken);
            }
        }

        _cache[datasetId] = manifest;
        return manifest;
    }

    /// <summary>
    /// Replaces the manifest: written to a temp file, then renamed over the old one. The rename
    /// is the commit point of every write to the dataset.
    /// </summary>
    public async Task SaveAsync(Guid datasetId, ParquetManifest manifest, CancellationToken cancellationToken)
    {
        string manifestPath = PathOf(ManifestFileName(datasetId));
        string tempPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, manifestPath, overwrite: true);
            _cache[datasetId] = manifest;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Removes the manifest first (so nothing is live), then every file of the dataset.</summary>
    public void Delete(Guid datasetId)
    {
        _cache.TryRemove(datasetId, out _);
        TryDelete(PathOf(ManifestFileName(datasetId)));
        foreach (string file in Directory.GetFiles(_dataDirectory, $"dataset_{datasetId:N}_*"))
        {
            TryDelete(file);
        }
    }

    /// <summary>
    /// Deletes the dataset's files the manifest doesn't list: segments and indexes from a write
    /// that crashed before its commit, files replaced by compaction, and temp files.
    /// </summary>
    public void SweepOrphans(Guid datasetId, ParquetManifest manifest)
    {
        HashSet<string> live = new(StringComparer.Ordinal);
        foreach (ParquetSegment segment in manifest.Segments)
        {
            live.Add(segment.File);
            live.Add(SegmentIdIndex.FileNameFor(segment.File));
        }

        foreach (string file in Directory.GetFiles(_dataDirectory, $"dataset_{datasetId:N}*"))
        {
            string name = Path.GetFileName(file);
            if (name == ManifestFileName(datasetId) || live.Contains(name))
            {
                continue;
            }
            TryDelete(file);
        }
    }

    /// <summary>Every dataset that has items here (a manifest, or shard files from before manifests).</summary>
    public IReadOnlyCollection<Guid> ListDatasets()
    {
        HashSet<Guid> ids = new();
        foreach (string file in Directory.GetFiles(_dataDirectory, "dataset_*"))
        {
            string name = Path.GetFileName(file);
            if (name.Length >= 40 && Guid.TryParseExact(name.Substring(8, 32), "N", out Guid id)
                && (name.EndsWith(".manifest.json", StringComparison.Ordinal) || name.EndsWith(".parquet", StringComparison.Ordinal)))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private async Task<ParquetManifest> AdoptLegacyShardsAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        List<(int Index, string File)> shards = new();
        foreach (string path in Directory.GetFiles(_dataDirectory, $"dataset_{datasetId:N}_shard_*.parquet"))
        {
            if (ParquetSchemaDefinition.TryParseFileName(Path.GetFileName(path), out _, out int index))
            {
                shards.Add((index, Path.GetFileName(path)));
            }
        }

        List<ParquetSegment> segments = new();
        foreach ((int _, string file) in shards.OrderBy(s => s.Index))
        {
            await using FileStream stream = File.OpenRead(PathOf(file));
            using ParquetReader reader = await ParquetReader.CreateAsync(stream, ParquetSchemaDefinition.ReaderOptions, cancellationToken: cancellationToken);
            long rows = 0;
            for (int g = 0; g < reader.RowGroupCount; g++)
            {
                using ParquetRowGroupReader group = reader.OpenRowGroupReader(g);
                rows += group.RowCount;
            }
            segments.Add(new ParquetSegment(file, rows));
        }
        return new ParquetManifest(0, segments);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Swept again on the next write to the dataset.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
