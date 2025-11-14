using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Interface for parsing dataset files into structured DatasetItem collections</summary>
public interface IDatasetParser
{
    /// <summary>Gets the format type this parser handles</summary>
    DatasetFormat FormatType { get; }
    
    /// <summary>Gets the modality type this parser produces</summary>
    Modality ModalityType { get; }
    
    /// <summary>Gets human-readable name of this parser</summary>
    string Name { get; }
    
    /// <summary>Gets description of what this parser does</summary>
    string Description { get; }
    
    /// <summary>Checks if this parser can handle the given file based on structure/content analysis</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <param name="fileName">Original file name for extension checking</param>
    /// <returns>True if this parser can handle the file, false otherwise</returns>
    bool CanParse(string fileContent, string fileName);
    
    /// <summary>Parses the file content and yields dataset items for memory-efficient streaming</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <param name="datasetId">ID of the parent dataset</param>
    /// <param name="options">Optional parsing configuration</param>
    /// <returns>Async enumerable of parsed dataset items</returns>
    IAsyncEnumerable<IDatasetItem> ParseAsync(string fileContent, string datasetId, Dictionary<string, string>? options = null);
    
    /// <summary>Validates file content before parsing to catch errors early</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <returns>Validation result with errors if any</returns>
    (bool IsValid, List<string> Errors) Validate(string fileContent);
    
    /// <summary>Gets estimated item count without full parsing (for progress indication)</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <returns>Estimated number of items that will be parsed</returns>
    int EstimateItemCount(string fileContent);
    
    // TODO: Add support for parsing from stream instead of full file content
    // TODO: Add support for incremental parsing (pause/resume)
    // TODO: Add support for parsing configuration schema (dynamic settings per parser)
}
