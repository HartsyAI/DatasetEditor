namespace HartsysDatasetEditor.Contracts.Datasets;

/// <summary>
/// Response containing available streaming and download options for a HuggingFace dataset.
/// </summary>
public sealed record HuggingFaceDiscoveryResponse
{
    /// <summary>Dataset repository identifier.</summary>
    public string Repository { get; init; } = string.Empty;

    /// <summary>Whether the dataset exists and is accessible.</summary>
    public bool IsAccessible { get; init; }

    /// <summary>Error message if dataset is not accessible.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Basic dataset metadata.</summary>
    public HuggingFaceDatasetMetadata? Metadata { get; init; }

    /// <summary>Streaming options available via datasets-server API.</summary>
    public HuggingFaceStreamingOptions? StreamingOptions { get; init; }

    /// <summary>Download options for datasets with local files.</summary>
    public HuggingFaceDownloadOptions? DownloadOptions { get; init; }
}

/// <summary>Basic metadata about the HuggingFace dataset.</summary>
public sealed record HuggingFaceDatasetMetadata
{
    public string Id { get; init; } = string.Empty;
    
    public string Author { get; init; } = string.Empty;
    
    public bool IsPrivate { get; init; }
    
    public bool IsGated { get; init; }
    
    public List<string> Tags { get; init; } = new();
    
    public int FileCount { get; init; }
}

/// <summary>Streaming options available for the dataset.</summary>
public sealed record HuggingFaceStreamingOptions
{
    /// <summary>Whether streaming is supported via datasets-server.</summary>
    public bool IsSupported { get; init; }

    /// <summary>Reason if streaming is not supported.</summary>
    public string? UnsupportedReason { get; init; }

    /// <summary>Recommended config/split for streaming (auto-selected).</summary>
    public HuggingFaceConfigOption? RecommendedOption { get; init; }

    /// <summary>All available config/split combinations.</summary>
    public List<HuggingFaceConfigOption> AvailableOptions { get; init; } = new();
}

/// <summary>A specific config/split combination available for streaming.</summary>
public sealed record HuggingFaceConfigOption
{
    /// <summary>Configuration name (subset), or null for default.</summary>
    public string? Config { get; init; }

    /// <summary>Split name (e.g., "train", "test", "validation").</summary>
    public string Split { get; init; } = string.Empty;

    /// <summary>Number of rows in this config/split.</summary>
    public long? NumRows { get; init; }

    /// <summary>Whether this is the recommended default option.</summary>
    public bool IsRecommended { get; set; }

    /// <summary>Display label for UI.</summary>
    public string DisplayLabel { get; init; } = string.Empty;
}

/// <summary>Download options for datasets with data files.</summary>
public sealed record HuggingFaceDownloadOptions
{
    /// <summary>Whether download mode is available.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Primary data file to download (auto-selected).</summary>
    public HuggingFaceDataFileOption? PrimaryFile { get; init; }

    /// <summary>All available data files.</summary>
    public List<HuggingFaceDataFileOption> AvailableFiles { get; init; } = new();

    /// <summary>Whether the dataset has image files only (no data files).</summary>
    public bool HasImageFilesOnly { get; init; }

    /// <summary>Count of image files if HasImageFilesOnly is true.</summary>
    public int ImageFileCount { get; init; }
}

/// <summary>A data file available for download.</summary>
public sealed record HuggingFaceDataFileOption
{
    /// <summary>File path in the repository.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>File type (csv, json, parquet).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Whether this is the recommended primary file.</summary>
    public bool IsPrimary { get; init; }
}
