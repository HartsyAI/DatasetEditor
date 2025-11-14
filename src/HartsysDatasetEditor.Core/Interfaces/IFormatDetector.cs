using HartsysDatasetEditor.Core.Enums;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Interface for automatic detection of dataset formats</summary>
public interface IFormatDetector
{
    /// <summary>Detects the format of a dataset file by analyzing its content and structure</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <param name="fileName">Original file name for extension hints</param>
    /// <returns>Detected dataset format, or Unknown if cannot determine</returns>
    DatasetFormat DetectFormat(string fileContent, string fileName);
    
    /// <summary>Detects the format with confidence score</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <param name="fileName">Original file name for extension hints</param>
    /// <returns>Tuple of detected format and confidence score (0.0 to 1.0)</returns>
    (DatasetFormat Format, double Confidence) DetectFormatWithConfidence(string fileContent, string fileName);
    
    /// <summary>Gets all possible formats ordered by likelihood</summary>
    /// <param name="fileContent">Raw file content as string</param>
    /// <param name="fileName">Original file name for extension hints</param>
    /// <returns>List of possible formats with confidence scores, ordered by confidence descending</returns>
    List<(DatasetFormat Format, double Confidence)> GetPossibleFormats(string fileContent, string fileName);
    
    // TODO: Add support for format detection from file streams (without loading full content)
    // TODO: Add support for custom format detection rules registration
}
