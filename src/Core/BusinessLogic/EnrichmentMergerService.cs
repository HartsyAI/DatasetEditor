using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Items;
using DatasetStudio.Core.Utilities.Logging;
using CsvHelper;
using System.Globalization;

namespace DatasetStudio.Core.BusinessLogic;

/// <summary>Merges enrichment file data into primary dataset items</summary>
public class EnrichmentMergerService
{
    /// <summary>Merges enrichment data into a list of items</summary>
    public async Task<List<IDatasetItem>> MergeEnrichmentsAsync(
        List<IDatasetItem> primaryItems,
        List<EnrichmentFile> enrichmentFiles)
    {
        foreach (EnrichmentFile enrichment in enrichmentFiles)
        {
            Logs.Info($"Merging enrichment: {enrichment.FileName} ({enrichment.Info.EnrichmentType})");

            try
            {
                await MergeEnrichmentFileAsync(primaryItems, enrichment);
                enrichment.Info.Applied = true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to merge enrichment {enrichment.FileName}", ex);
                enrichment.Info.Errors.Add(ex.Message);
                enrichment.Info.Applied = false;
            }
        }

        return primaryItems;
    }

    /// <summary>Merges a single enrichment file into items</summary>
    public async Task MergeEnrichmentFileAsync(
        List<IDatasetItem> items,
        EnrichmentFile enrichment)
    {
        // Parse enrichment file into dictionary keyed by foreign key
        Dictionary<string, Dictionary<string, string>> enrichmentData =
            await ParseEnrichmentDataAsync(enrichment);

        // Merge into items
        foreach (IDatasetItem item in items)
        {
            if (enrichmentData.TryGetValue(item.Id, out Dictionary<string, string>? rowData))
            {
                MergeRowIntoItem(item, rowData, enrichment.Info.EnrichmentType);
            }
        }

        Logs.Info($"Merged {enrichmentData.Count} enrichment records into items");
    }

    /// <summary>Parses enrichment file into a lookup dictionary</summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> ParseEnrichmentDataAsync(
        EnrichmentFile enrichment)
    {
        Dictionary<string, Dictionary<string, string>> data = new();

        using StringReader reader = new(enrichment.Content);
        using CsvReader csv = new(reader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();

        string fkColumn = enrichment.Info.ForeignKeyColumn;

        while (await csv.ReadAsync())
        {
            string? foreignKey = csv.GetField<string>(fkColumn);
            if (string.IsNullOrEmpty(foreignKey))
                continue;

            Dictionary<string, string> rowData = new();

            foreach (string column in enrichment.Info.ColumnsToMerge)
            {
                string? value = csv.GetField<string>(column);
                if (!string.IsNullOrEmpty(value))
                {
                    rowData[column] = value;
                }
            }

            data[foreignKey] = rowData;
        }

        return data;
    }

    /// <summary>Merges a row of enrichment data into an item</summary>
    public void MergeRowIntoItem(
        IDatasetItem item,
        Dictionary<string, string> rowData,
        string enrichmentType)
    {
        if (item is not ImageItem imageItem)
            return;

        switch (enrichmentType)
        {
            case "colors":
                MergeColorData(imageItem, rowData);
                break;

            case "tags":
                MergeTagData(imageItem, rowData);
                break;

            case "collections":
                MergeCollectionData(imageItem, rowData);
                break;

            default:
                // Generic metadata merge
                foreach (KeyValuePair<string, string> kvp in rowData)
                {
                    imageItem.Metadata[kvp.Key] = kvp.Value;
                }
                break;
        }
    }

    public void MergeColorData(ImageItem item, Dictionary<string, string> data)
    {
        // Example Unsplash colors.csv structure:
        // photo_id, hex, red, green, blue, keyword

        if (data.TryGetValue("hex", out string? hexColor))
        {
            item.AverageColor = hexColor;
        }

        // Add all color hex values to dominant colors
        List<string> colorColumns = data.Keys
            .Where(k => k.Contains("hex", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (string colorColumn in colorColumns)
        {
            if (data.TryGetValue(colorColumn, out string? color) && !string.IsNullOrEmpty(color))
            {
                if (!item.DominantColors.Contains(color))
                {
                    item.DominantColors.Add(color);
                }
            }
        }

        // Store full color data in metadata
        foreach (KeyValuePair<string, string> kvp in data)
        {
            item.Metadata[$"color_{kvp.Key}"] = kvp.Value;
        }
    }

    public void MergeTagData(ImageItem item, Dictionary<string, string> data)
    {
        foreach (KeyValuePair<string, string> kvp in data)
        {
            if (kvp.Key.Contains("tag", StringComparison.OrdinalIgnoreCase))
            {
                // Split by comma if multiple tags in one column
                string[] tags = kvp.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);

                foreach (string tag in tags)
                {
                    string cleanTag = tag.Trim();
                    if (!string.IsNullOrEmpty(cleanTag) && !item.Tags.Contains(cleanTag))
                    {
                        item.Tags.Add(cleanTag);
                    }
                }
            }
        }
    }

    public void MergeCollectionData(ImageItem item, Dictionary<string, string> data)
    {
        foreach (KeyValuePair<string, string> kvp in data)
        {
            if (kvp.Key.Contains("collection", StringComparison.OrdinalIgnoreCase))
            {
                // Add collection names as tags
                string collectionName = kvp.Value.Trim();
                if (!string.IsNullOrEmpty(collectionName) && !item.Tags.Contains(collectionName))
                {
                    item.Tags.Add(collectionName);
                }
            }

            // Store in metadata
            item.Metadata[$"collection_{kvp.Key}"] = kvp.Value;
        }
    }
}
