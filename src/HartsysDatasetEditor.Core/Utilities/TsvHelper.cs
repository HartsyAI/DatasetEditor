namespace HartsysDatasetEditor.Core.Utilities;

/// <summary>Helper utilities for working with TSV files</summary>
public static class TsvHelper
{
    /// <summary>Parses a TSV line into an array of values</summary>
    public static string[] ParseLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return Array.Empty<string>();
        }
        
        return line.Split('\t').Select(v => v.Trim()).ToArray();
    }
    
    /// <summary>Escapes a value for TSV format (handles tabs and newlines)</summary>
    public static string EscapeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        
        // Replace tabs with spaces
        value = value.Replace('\t', ' ');
        
        // Replace newlines with spaces
        value = value.Replace('\n', ' ').Replace('\r', ' ');
        
        return value.Trim();
    }
    
    /// <summary>Creates a TSV line from an array of values</summary>
    public static string CreateLine(params string[] values)
    {
        return string.Join('\t', values.Select(EscapeValue));
    }
    
    /// <summary>Reads all lines from TSV content, splitting by newline</summary>
    public static string[] ReadLines(string tsvContent)
    {
        if (string.IsNullOrWhiteSpace(tsvContent))
        {
            return Array.Empty<string>();
        }
        
        return tsvContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    }
    
    // TODO: Add support for quoted fields (CSV-style quoting)
    // TODO: Add support for different delimiters
    // TODO: Add support for detecting encoding
}
