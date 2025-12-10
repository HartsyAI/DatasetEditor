using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.Abstractions.Parsers;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.Core.BusinessLogic.Parsers;

/// <summary>Base class for all TSV (Tab-Separated Values) parsers providing common parsing logic</summary>
public abstract class BaseTsvParser : IDatasetParser
{
    /// <summary>Gets the format type this parser handles</summary>
    public virtual DatasetFormat FormatType => DatasetFormat.TSV;

    /// <summary>Gets the modality type this parser produces</summary>
    public abstract Modality ModalityType { get; }

    /// <summary>Gets human-readable name of this parser</summary>
    public abstract string Name { get; }

    /// <summary>Gets description of what this parser does</summary>
    public abstract string Description { get; }

    /// <summary>Checks if this parser can handle the given file</summary>
    public virtual bool CanParse(string fileContent, string fileName)
    {
        // Check file extension
        if (!fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".tsv000", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".csv000", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if content has tab-separated structure
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        string[] lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) // Need at least header + one data row
        {
            return false;
        }

        // Check if first line has tabs (header row)
        return lines[0].Contains('\t');
    }

    /// <summary>Parses TSV content and yields dataset items</summary>
    public abstract IAsyncEnumerable<IDatasetItem> ParseAsync(string fileContent, string datasetId, Dictionary<string, string>? options = null);

    /// <summary>Validates TSV file structure</summary>
    public virtual (bool IsValid, List<string> Errors) Validate(string fileContent)
    {
        List<string> errors = new();

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            errors.Add("File content is empty");
            return (false, errors);
        }

        string[] lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            errors.Add("File must contain at least a header row and one data row");
            return (false, errors);
        }

        // Validate header row has tabs
        if (!lines[0].Contains('\t'))
        {
            errors.Add("Header row does not contain tab separators");
        }

        // Get expected column count from header
        int expectedColumns = lines[0].Split('\t').Length;

        // Validate all rows have same column count
        for (int i = 1; i < Math.Min(lines.Length, 100); i++) // Check first 100 rows for performance
        {
            int columnCount = lines[i].Split('\t').Length;
            if (columnCount != expectedColumns)
            {
                errors.Add($"Row {i + 1} has {columnCount} columns but expected {expectedColumns}");
            }
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>Estimates item count by counting non-header lines</summary>
    public virtual int EstimateItemCount(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return 0;
        }

        // Count lines and subtract 1 for header
        int lineCount = fileContent.Count(c => c == '\n');
        return Math.Max(0, lineCount - 1);
    }

    /// <summary>Parses TSV header row and returns column names</summary>
    protected string[] ParseHeader(string headerLine)
    {
        return headerLine.Split('\t')
            .Select(h => h.Trim())
            .ToArray();
    }

    /// <summary>Parses TSV data row and returns cell values</summary>
    protected string[] ParseRow(string dataRow)
    {
        return dataRow.Split('\t')
            .Select(v => v.Trim())
            .ToArray();
    }

    /// <summary>Safely gets column value by name from parsed row</summary>
    protected string GetColumnValue(string[] headers, string[] values, string columnName, string defaultValue = "")
    {
        int index = Array.IndexOf(headers, columnName);
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }
        return defaultValue;
    }

    /// <summary>Safely parses integer from column value</summary>
    protected int GetIntValue(string[] headers, string[] values, string columnName, int defaultValue = 0)
    {
        string value = GetColumnValue(headers, values, columnName);
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    /// <summary>Safely parses long from column value</summary>
    protected long GetLongValue(string[] headers, string[] values, string columnName, long defaultValue = 0)
    {
        string value = GetColumnValue(headers, values, columnName);
        return long.TryParse(value, out long result) ? result : defaultValue;
    }

    /// <summary>Safely parses double from column value</summary>
    protected double GetDoubleValue(string[] headers, string[] values, string columnName, double defaultValue = 0.0)
    {
        string value = GetColumnValue(headers, values, columnName);
        return double.TryParse(value, out double result) ? result : defaultValue;
    }

    /// <summary>Safely parses DateTime from column value</summary>
    protected DateTime? GetDateTimeValue(string[] headers, string[] values, string columnName)
    {
        string value = GetColumnValue(headers, values, columnName);
        return DateTime.TryParse(value, out DateTime result) ? result : null;
    }

    // TODO: Add support for quoted fields with embedded tabs
    // TODO: Add support for escaped characters
    // TODO: Add support for different encodings (UTF-8, UTF-16, etc.)
    // TODO: Add support for custom delimiters (not just tabs)
}
