namespace DatasetStudio.APIBackend.Services.DatasetManagement.Parsing;

/// <summary>One row read from an uploaded Parquet file.</summary>
/// <param name="Values">Lower-case column name to its value as text, for every flat column.</param>
/// <param name="ImageBytes">
/// The bytes of an embedded image, when the file stores images inline (HuggingFace's
/// <c>image: {bytes, path}</c> layout); otherwise null.
/// </param>
/// <param name="ImageName">The embedded image's original file name, when the file records one.</param>
public sealed record ParquetRow(Dictionary<string, string> Values, byte[]? ImageBytes, string? ImageName);
