using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Core.Services.Parsers;

/// <summary>Registry for managing and discovering dataset parsers. Implements provider/plugin pattern for extensibility.</summary>
public class ParserRegistry
{
    private readonly List<IDatasetParser> _parsers = new();
    
    /// <summary>Initializes the registry and registers all available parsers</summary>
    public ParserRegistry()
    {
        RegisterDefaultParsers();
    }
    
    /// <summary>Registers default built-in parsers</summary>
    private void RegisterDefaultParsers()
    {
        // Register Unsplash TSV parser
        Register(new UnsplashTsvParser());
        
        Logs.Info($"Registered {_parsers.Count} default parsers");
        
        // TODO: Auto-discover and register parsers using reflection
        // TODO: Load parsers from external assemblies/plugins
    }
    
    /// <summary>Registers a parser with the registry</summary>
    public void Register(IDatasetParser parser)
    {
        if (parser == null)
        {
            throw new ArgumentNullException(nameof(parser));
        }
        
        // Check if already registered
        if (_parsers.Any(p => p.GetType() == parser.GetType()))
        {
            Logs.Warning($"Parser {parser.Name} is already registered");
            return;
        }
        
        _parsers.Add(parser);
        Logs.Info($"Registered parser: {parser.Name} (Format: {parser.FormatType}, Modality: {parser.ModalityType})");
    }
    
    /// <summary>Unregisters a parser from the registry</summary>
    public void Unregister(IDatasetParser parser)
    {
        if (parser == null)
        {
            return;
        }
        
        _parsers.Remove(parser);
        Logs.Info($"Unregistered parser: {parser.Name}");
    }
    
    /// <summary>Gets all registered parsers</summary>
    public IReadOnlyList<IDatasetParser> GetAllParsers()
    {
        return _parsers.AsReadOnly();
    }
    
    /// <summary>Gets parsers that support a specific format</summary>
    public List<IDatasetParser> GetParsersByFormat(DatasetFormat format)
    {
        return _parsers.Where(p => p.FormatType == format).ToList();
    }
    
    /// <summary>Gets parsers that support a specific modality</summary>
    public List<IDatasetParser> GetParsersByModality(Modality modality)
    {
        return _parsers.Where(p => p.ModalityType == modality).ToList();
    }
    
    /// <summary>Finds the most appropriate parser for the given file content</summary>
    public IDatasetParser? FindParser(string fileContent, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            Logs.Warning("Cannot find parser: file content is empty");
            return null;
        }
        
        // Try each parser's CanParse method
        foreach (IDatasetParser parser in _parsers)
        {
            try
            {
                if (parser.CanParse(fileContent, fileName))
                {
                    Logs.Info($"Found compatible parser: {parser.Name}");
                    return parser;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error checking parser {parser.Name}: {ex.Message}", ex);
            }
        }
        
        Logs.Warning($"No compatible parser found for file: {fileName}");
        return null;
    }
    
    /// <summary>Finds all compatible parsers for the given file content (returns multiple if ambiguous)</summary>
    public List<IDatasetParser> FindAllCompatibleParsers(string fileContent, string fileName)
    {
        List<IDatasetParser> compatible = new();
        
        foreach (IDatasetParser parser in _parsers)
        {
            try
            {
                if (parser.CanParse(fileContent, fileName))
                {
                    compatible.Add(parser);
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error checking parser {parser.Name}: {ex.Message}", ex);
            }
        }
        
        Logs.Info($"Found {compatible.Count} compatible parsers for file: {fileName}");
        return compatible;
    }
    
    /// <summary>Gets a parser by its format type (returns first match)</summary>
    public IDatasetParser? GetParserByFormat(DatasetFormat format)
    {
        return _parsers.FirstOrDefault(p => p.FormatType == format);
    }
    
    /// <summary>Clears all registered parsers</summary>
    public void Clear()
    {
        int count = _parsers.Count;
        _parsers.Clear();
        Logs.Info($"Cleared {count} parsers from registry");
    }
    
    // TODO: Add support for parser priority/ordering when multiple parsers match
    // TODO: Add support for parser configuration/options
    // TODO: Add support for parser caching (cache parse results)
    // TODO: Add support for parser health checks
}
