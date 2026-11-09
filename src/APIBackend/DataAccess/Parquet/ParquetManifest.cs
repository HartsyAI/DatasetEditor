namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// The ordered list of segment files that make up a dataset's items. The manifest is the only
/// thing that says which files are live: a write creates new files first and commits by
/// replacing the manifest in one rename, so a crash at any point leaves the previous, complete
/// dataset (audit P-4, P-11).
/// </summary>
/// <param name="NextSegment">Sequence number for the next segment file name.</param>
/// <param name="Segments">Live segments in row order.</param>
public sealed record ParquetManifest(long NextSegment, IReadOnlyList<ParquetSegment> Segments)
{
    /// <summary>A dataset with no items.</summary>
    public static ParquetManifest Empty { get; } = new(0, Array.Empty<ParquetSegment>());

    /// <summary>Rows across all segments.</summary>
    public long TotalRows => Segments.Sum(s => s.Rows);

    /// <summary>The first row of each segment, as an absolute row offset.</summary>
    public long[] SegmentStarts()
    {
        long[] starts = new long[Segments.Count];
        long start = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            starts[i] = start;
            start += Segments[i].Rows;
        }
        return starts;
    }
}
