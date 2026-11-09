namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>One immutable Parquet file of a dataset, as listed in its manifest.</summary>
/// <param name="File">File name inside the Parquet directory.</param>
/// <param name="Rows">Rows in the file.</param>
public sealed record ParquetSegment(string File, long Rows);
