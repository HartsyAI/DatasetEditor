using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Abstractions.Parsers;
using DatasetStudio.Core.BusinessLogic.Parsers;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.Core.BusinessLogic;

/// <summary>Service for automatically detecting dataset formats from file content</summary>
public class FormatDetector : IFormatDetector
{
    private readonly ParserRegistry _parserRegistry;

    public FormatDetector(ParserRegistry parserRegistry)
    {
        _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
    }

    /// <summary>Detects the format of a dataset file</summary>
    public DatasetFormat DetectFormat(string fileContent, string fileName)
    {
        (DatasetFormat format, double confidence) = DetectFormatWithConfidence(fileContent, fileName);
        return format;
    }

    /// <summary>Detects the format with confidence score</summary>
    public (DatasetFormat Format, double Confidence) DetectFormatWithConfidence(string fileContent, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            Logs.Warning("Cannot detect format: file content is empty");
            return (DatasetFormat.Unknown, 0.0);
        }

        // Try each registered parser
        List<IDatasetParser> compatibleParsers = _parserRegistry.FindAllCompatibleParsers(fileContent, fileName);

        if (compatibleParsers.Count == 0)
        {
            Logs.Warning($"No compatible parsers found for file: {fileName}");
            return (DatasetFormat.Unknown, 0.0);
        }

        if (compatibleParsers.Count == 1)
        {
            Logs.Info($"Detected format: {compatibleParsers[0].FormatType} with high confidence");
            return (compatibleParsers[0].FormatType, 1.0);
        }

        // Multiple parsers match - calculate confidence scores
        // For MVP, just return the first match with medium confidence
        Logs.Info($"Multiple parsers match ({compatibleParsers.Count}), returning first: {compatibleParsers[0].FormatType}");
        return (compatibleParsers[0].FormatType, 0.7);

        // TODO: Implement sophisticated confidence scoring based on:
        // - File extension match weight
        // - Required fields presence
        // - Data structure validation
        // - Statistical analysis of content
    }

    /// <summary>Gets all possible formats ordered by likelihood</summary>
    public List<(DatasetFormat Format, double Confidence)> GetPossibleFormats(string fileContent, string fileName)
    {
        List<(DatasetFormat Format, double Confidence)> results = new();

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return results;
        }

        List<IDatasetParser> compatibleParsers = _parserRegistry.FindAllCompatibleParsers(fileContent, fileName);

        foreach (IDatasetParser parser in compatibleParsers)
        {
            // For MVP, assign equal confidence to all matches
            double confidence = 1.0 / compatibleParsers.Count;
            results.Add((parser.FormatType, confidence));
        }

        // Sort by confidence descending
        return results.OrderByDescending(r => r.Confidence).ToList();

        // TODO: Implement sophisticated ranking algorithm
    }
}
