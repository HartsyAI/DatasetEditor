using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;
using CsvHelper;
using System.Globalization;

namespace HartsysDatasetEditor.Core.Services;

/// <summary>Detects primary dataset files and enrichment files in multi-file uploads</summary>
public class MultiFileDetectorService
{
    /// <summary>Analyzes a collection of files and determines which is primary and which are enrichments</summary>
    public DatasetFileCollection AnalyzeFiles(Dictionary<string, string> files)
    {
        DatasetFileCollection collection = new();
        
        // Step 1: Detect primary file (has image URLs or required fields)
        KeyValuePair<string, string>? primaryFile = DetectPrimaryFile(files);
        
        if (primaryFile == null)
        {
            Logs.Error("Could not detect primary dataset file");
            return collection;
        }
        
        collection.PrimaryFileName = primaryFile.Value.Key;
        collection.PrimaryFileContent = primaryFile.Value.Value;
        
        Logs.Info($"Primary file detected: {collection.PrimaryFileName}");
        
        // Step 2: Analyze remaining files as potential enrichments
        foreach (KeyValuePair<string, string> file in files)
        {
            if (file.Key == collection.PrimaryFileName)
                continue;
            
            EnrichmentFile enrichment = AnalyzeEnrichmentFile(file.Key, file.Value);
            if (enrichment.Info.ForeignKeyColumn != string.Empty)
            {
                collection.EnrichmentFiles.Add(enrichment);
                Logs.Info($"Enrichment file detected: {file.Key} (type: {enrichment.Info.EnrichmentType})");
            }
        }
        
        collection.TotalSizeBytes = files.Sum(f => f.Value.Length);
        
        return collection;
    }
    
    /// <summary>Detects which file is the primary dataset file</summary>
    public KeyValuePair<string, string>? DetectPrimaryFile(Dictionary<string, string> files)
    {
        foreach (KeyValuePair<string, string> file in files)
        {
            // Check if file has image URL columns
            if (HasImageUrlColumn(file.Value))
            {
                return file;
            }
        }
        
        // Fallback: return largest file
        return files.OrderByDescending(f => f.Value.Length).FirstOrDefault();
    }
    
    /// <summary>Checks if a file contains image URL columns</summary>
    public bool HasImageUrlColumn(string content)
    {
        try
        {
            using StringReader reader = new(content);
            using CsvReader csv = new(reader, CultureInfo.InvariantCulture);
            
            csv.Read();
            csv.ReadHeader();
            
            if (csv.HeaderRecord == null)
                return false;
            
            // Look for common image URL column names
            string[] imageUrlColumns = { "photo_image_url", "image_url", "url", "imageurl", "photo_url", "img_url" };
            
            return csv.HeaderRecord.Any(h => imageUrlColumns.Contains(h.ToLowerInvariant()));
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>Analyzes a file to determine if it's an enrichment file</summary>
    public EnrichmentFile AnalyzeEnrichmentFile(string fileName, string content)
    {
        EnrichmentFile enrichment = new()
        {
            FileName = fileName,
            Content = content,
            SizeBytes = content.Length
        };
        
        try
        {
            using StringReader reader = new(content);
            using CsvReader csv = new(reader, CultureInfo.InvariantCulture);
            
            csv.Read();
            csv.ReadHeader();
            
            if (csv.HeaderRecord == null)
                return enrichment;
            
            // Detect enrichment type based on filename and columns
            if (fileName.Contains("color", StringComparison.OrdinalIgnoreCase))
            {
                enrichment.Info.EnrichmentType = "colors";
                enrichment.Info.ForeignKeyColumn = DetectForeignKeyColumn(csv.HeaderRecord);
                enrichment.Info.ColumnsToMerge = csv.HeaderRecord
                    .Where(h => h.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                               h.Contains("hex", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (fileName.Contains("tag", StringComparison.OrdinalIgnoreCase))
            {
                enrichment.Info.EnrichmentType = "tags";
                enrichment.Info.ForeignKeyColumn = DetectForeignKeyColumn(csv.HeaderRecord);
                enrichment.Info.ColumnsToMerge = csv.HeaderRecord
                    .Where(h => h.Contains("tag", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (fileName.Contains("collection", StringComparison.OrdinalIgnoreCase))
            {
                enrichment.Info.EnrichmentType = "collections";
                enrichment.Info.ForeignKeyColumn = DetectForeignKeyColumn(csv.HeaderRecord);
                enrichment.Info.ColumnsToMerge = csv.HeaderRecord
                    .Where(h => h.Contains("collection", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                // Generic enrichment
                enrichment.Info.EnrichmentType = "metadata";
                enrichment.Info.ForeignKeyColumn = DetectForeignKeyColumn(csv.HeaderRecord);
                enrichment.Info.ColumnsToMerge = csv.HeaderRecord.ToList();
            }
            
            // Count records
            int count = 0;
            while (csv.Read())
            {
                count++;
            }
            enrichment.Info.RecordCount = count;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to analyze enrichment file {fileName}", ex);
            enrichment.Info.Errors.Add(ex.Message);
        }
        
        return enrichment;
    }
    
    /// <summary>Detects which column is the foreign key linking to primary dataset</summary>
    public string DetectForeignKeyColumn(string[] headers)
    {
        // Common foreign key column names
        string[] fkColumns = { "photo_id", "image_id", "id", "item_id", "photoid", "imageid" };
        
        foreach (string header in headers)
        {
            if (fkColumns.Contains(header.ToLowerInvariant()))
            {
                return header;
            }
        }
        
        // Default to first column if no match
        return headers.Length > 0 ? headers[0] : string.Empty;
    }
}
