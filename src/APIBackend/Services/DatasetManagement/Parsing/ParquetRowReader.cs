using System.Globalization;
using System.Runtime.CompilerServices;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace DatasetStudio.APIBackend.Services.DatasetManagement.Parsing;

/// <summary>
/// Reads an uploaded or downloaded Parquet file one row group at a time. There was no Parquet
/// parser, and HuggingFace download mode picks a <c>.parquet</c> file first, so most downloads
/// failed with NotSupportedException after the whole file had been fetched (audit I-6).
/// </summary>
/// <remarks>
/// Flat columns become text values. A struct column whose children are <c>bytes</c> and
/// <c>path</c> (how HuggingFace stores an <c>Image</c> feature) is read as an embedded image.
/// Repeated (list) and other nested columns are skipped: items have no place to put them.
/// Memory is bounded by one row group, which is how Parquet files are meant to be read.
/// </remarks>
public static class ParquetRowReader
{
    /// <summary>Column names tried, in order, for the embedded image when there are several.</summary>
    private static readonly string[] PreferredImageColumns = { "image", "img", "picture", "photo" };

    /// <summary>Reads every row. The stream must be seekable (Parquet's footer is at the end).</summary>
    public static async IAsyncEnumerable<ParquetRow> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using ParquetReader reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);
        DataField[] fields = reader.Schema.GetDataFields();

        List<DataField> flatFields = fields
            .Where(f => f.Path.Length == 1 && f.MaxRepetitionLevel == 0)
            .ToList();
        (DataField Bytes, DataField? Path)? imageFields = FindImageFields(fields);

        for (int groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            using ParquetRowGroupReader group = reader.OpenRowGroupReader(groupIndex);
            int rowCount = checked((int)group.RowCount);

            List<(string Name, Array Data)> columns = new();
            foreach (DataField field in flatFields)
            {
                DataColumn column = await group.ReadColumnAsync(field, cancellationToken);
                if (column.Data.Length == rowCount)
                {
                    columns.Add((field.Name.ToLowerInvariant(), column.Data));
                }
            }

            Array? imageBytes = null;
            Array? imageNames = null;
            if (imageFields is { } image)
            {
                DataColumn bytesColumn = await group.ReadColumnAsync(image.Bytes, cancellationToken);
                imageBytes = bytesColumn.Data.Length == rowCount ? bytesColumn.Data : null;
                if (image.Path is not null)
                {
                    DataColumn pathColumn = await group.ReadColumnAsync(image.Path, cancellationToken);
                    imageNames = pathColumn.Data.Length == rowCount ? pathColumn.Data : null;
                }
            }

            for (int row = 0; row < rowCount; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, string> values = new();
                foreach ((string name, Array data) in columns)
                {
                    string? text = ToText(data.GetValue(row));
                    if (text is not null)
                    {
                        values[name] = text;
                    }
                }

                yield return new ParquetRow(
                    values,
                    imageBytes?.GetValue(row) as byte[],
                    imageNames?.GetValue(row) as string);
            }
        }
    }

    /// <summary>Finds an <c>{bytes, path}</c> struct column, preferring the usual image column names.</summary>
    private static (DataField Bytes, DataField? Path)? FindImageFields(DataField[] fields)
    {
        List<DataField> bytesFields = fields
            .Where(f => f.Path.Length == 2 && f.MaxRepetitionLevel == 0
                && string.Equals(f.Name, "bytes", StringComparison.OrdinalIgnoreCase)
                && f.ClrType == typeof(byte[]))
            .ToList();
        if (bytesFields.Count == 0)
        {
            return null;
        }

        DataField chosen = bytesFields
            .OrderBy(f =>
            {
                int index = Array.FindIndex(PreferredImageColumns, n => string.Equals(n, f.Path[0], StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .First();
        DataField? path = fields.FirstOrDefault(f => f.Path.Length == 2
            && f.Path[0] == chosen.Path[0]
            && string.Equals(f.Name, "path", StringComparison.OrdinalIgnoreCase)
            && f.ClrType == typeof(string));
        return (chosen, path);
    }

    private static string? ToText(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            byte[] => null,
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
