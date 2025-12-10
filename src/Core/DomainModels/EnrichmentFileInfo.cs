namespace DatasetStudio.Core.DomainModels;

/// <summary>Information about an enrichment file that supplements a primary dataset</summary>
public class EnrichmentFileInfo
{
    /// <summary>File name</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Type of enrichment (colors, tags, metadata, etc.)</summary>
    public string EnrichmentType { get; set; } = string.Empty;

    /// <summary>Foreign key column name that links to primary dataset</summary>
    public string ForeignKeyColumn { get; set; } = string.Empty;

    /// <summary>Columns to merge into primary items</summary>
    public List<string> ColumnsToMerge { get; set; } = new();

    /// <summary>Total records in enrichment file</summary>
    public int RecordCount { get; set; }

    /// <summary>Whether this enrichment was successfully applied</summary>
    public bool Applied { get; set; }

    /// <summary>Any errors encountered during merge</summary>
    public List<string> Errors { get; set; } = new();
}
