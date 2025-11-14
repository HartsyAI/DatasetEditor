namespace HartsysDatasetEditor.Core.Models;

/// <summary>Represents generic metadata with type information for extensibility</summary>
public class Metadata
{
    /// <summary>Metadata key/field name</summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>Metadata value as string (can be parsed to appropriate type)</summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>Data type of the value (string, int, double, bool, date, etc.)</summary>
    public string ValueType { get; set; } = "string";
    
    /// <summary>Optional display label for UI rendering</summary>
    public string DisplayLabel { get; set; } = string.Empty;
    
    /// <summary>Optional description or help text</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>Whether this field should be searchable</summary>
    public bool IsSearchable { get; set; } = true;
    
    /// <summary>Whether this field should be filterable</summary>
    public bool IsFilterable { get; set; } = true;
    
    /// <summary>Sort order for display (lower numbers first)</summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>Category for grouping related metadata fields</summary>
    public string Category { get; set; } = "General";
    
    // TODO: Add validation rules when implementing dynamic settings system
    // TODO: Add UI hints (text input, dropdown, slider, etc.)
    // TODO: Add support for nested/hierarchical metadata
}
