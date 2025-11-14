using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Services.Parsers;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Core.Services;

/// <summary>Service for loading datasets from files, orchestrating format detection and parsing</summary>
public class DatasetLoader(ParserRegistry parserRegistry, FormatDetector formatDetector)
{
    private readonly ParserRegistry _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
    private readonly FormatDetector _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
    
    /// <summary>
    /// Loads a dataset from file content, automatically detecting format.
    /// </summary>
    public async Task<(Dataset Dataset, IAsyncEnumerable<IDatasetItem> Items)> LoadDatasetAsync(
        string fileContent,
        string fileName,
        string? datasetName = null)
    {
        Logs.Info($"Loading dataset from file: {fileName}");
        
        // Detect format
        DatasetFormat format = _formatDetector.DetectFormat(fileContent, fileName);
        
        if (format == DatasetFormat.Unknown)
        {
            throw new InvalidOperationException($"Unable to detect format for file: {fileName}");
        }
        
        Logs.Info($"Detected format: {format}");
        
        // Find appropriate parser
        IDatasetParser? parser = _parserRegistry.GetParserByFormat(format);
        
        if (parser == null)
        {
            throw new InvalidOperationException($"No parser available for format: {format}");
        }
        
        // Validate file content
        (bool isValid, List<string> errors) = parser.Validate(fileContent);
        
        if (!isValid)
        {
            string errorMessage = $"Validation failed: {string.Join(", ", errors)}";
            Logs.Error(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
        
        // Create dataset metadata
        Dataset dataset = new Dataset
        {
            Name = datasetName ?? Path.GetFileNameWithoutExtension(fileName),
            Format = format,
            Modality = parser.ModalityType,
            SourcePath = fileName,
            TotalItems = parser.EstimateItemCount(fileContent)
        };
        
        Logs.Info($"Created dataset: {dataset.Name} ({dataset.TotalItems} estimated items)");
        
        // Parse items (returns IAsyncEnumerable for streaming)
        IAsyncEnumerable<IDatasetItem> items = parser.ParseAsync(fileContent, dataset.Id);
        
        return (dataset, items);
    }

    /// <summary>
    /// Convenience wrapper used by Blazor client to load datasets from text content.
    /// TODO: Replace callers with direct <see cref="LoadDatasetAsync(string,string,string?)"/> usage when client handles metadata tuple natively.
    /// </summary>
    public Task<(Dataset Dataset, IAsyncEnumerable<IDatasetItem> Items)> LoadDatasetFromTextAsync(
        string fileContent,
        string fileName,
        string? datasetName = null)
    {
        // TODO: Support stream-based overloads so large TSVs don’t require reading entire file into memory.
        return LoadDatasetAsync(fileContent, fileName, datasetName);
    }
    
    /// <summary>Loads a dataset with explicit format specification</summary>
    public async Task<(Dataset Dataset, IAsyncEnumerable<IDatasetItem> Items)> LoadDatasetAsync(
        string fileContent,
        string fileName,
        DatasetFormat format,
        string? datasetName = null)
    {
        Logs.Info($"Loading dataset from file: {fileName} with specified format: {format}");
        
        // Find appropriate parser
        IDatasetParser? parser = _parserRegistry.GetParserByFormat(format);
        
        if (parser == null)
        {
            throw new InvalidOperationException($"No parser available for format: {format}");
        }
        
        // Validate file content
        (bool isValid, List<string> errors) = parser.Validate(fileContent);
        
        if (!isValid)
        {
            string errorMessage = $"Validation failed: {string.Join(", ", errors)}";
            Logs.Error(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
        
        // Create dataset metadata
        Dataset dataset = new Dataset
        {
            Name = datasetName ?? Path.GetFileNameWithoutExtension(fileName),
            Format = format,
            Modality = parser.ModalityType,
            SourcePath = fileName,
            TotalItems = parser.EstimateItemCount(fileContent)
        };
        
        // Parse items
        IAsyncEnumerable<IDatasetItem> items = parser.ParseAsync(fileContent, dataset.Id);
        
        return (dataset, items);
    }
    
    // TODO: Add support for loading from stream instead of full file content
    // TODO: Add support for progress callbacks during loading
    // TODO: Add support for cancellation tokens
    // TODO: Add support for partial loading (load first N items)
    // TODO: Add support for background loading
}
