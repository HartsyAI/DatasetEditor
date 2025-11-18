namespace HartsysDatasetEditor.Core.Models;

/// <summary>Collection of files that make up a complete dataset (primary + enrichments)</summary>
public class DatasetFileCollection
{
    /// <summary>Primary dataset file (contains core records)</summary>
    public string PrimaryFileName { get; set; } = string.Empty;
    
    /// <summary>Content of primary file</summary>
    public string PrimaryFileContent { get; set; } = string.Empty;
    
    /// <summary>Enrichment files</summary>
    public List<EnrichmentFile> EnrichmentFiles { get; set; } = new();
    
    /// <summary>Detected dataset format</summary>
    public string DetectedFormat { get; set; } = string.Empty;
    
    /// <summary>Total size of all files in bytes</summary>
    public long TotalSizeBytes { get; set; }
}

/// <summary>An enrichment file with its content</summary>
public class EnrichmentFile
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public EnrichmentFileInfo Info { get; set; } = new();
}
