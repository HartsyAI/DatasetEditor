using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.Core.BusinessLogic;

/// <summary>Service for searching dataset items using full-text search</summary>
public class SearchService
{
    /// <summary>Performs a full-text search on dataset items</summary>
    public List<IDatasetItem> Search(List<IDatasetItem> items, string query, int maxResults = 100)
    {
        if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return new List<IDatasetItem>();
        }

        Logs.Info($"Searching {items.Count} items for query: {query}");

        string searchQuery = query.ToLowerInvariant().Trim();
        string[] searchTerms = searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Score each item based on search relevance
        List<(IDatasetItem Item, double Score)> scoredItems = items
            .Select(item => (Item: item, Score: CalculateRelevanceScore(item, searchTerms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .ToList();

        Logs.Info($"Found {scoredItems.Count} matching items");

        return scoredItems.Select(x => x.Item).ToList();
    }

    /// <summary>Calculates relevance score for an item based on search terms</summary>
    private double CalculateRelevanceScore(IDatasetItem item, string[] searchTerms)
    {
        double score = 0.0;

        string title = item.Title.ToLowerInvariant();
        string description = item.Description.ToLowerInvariant();
        List<string> tags = item.Tags.Select(t => t.ToLowerInvariant()).ToList();

        foreach (string term in searchTerms)
        {
            // Title match has highest weight
            if (title.Contains(term))
            {
                score += 10.0;
                // Exact match bonus
                if (title == term)
                {
                    score += 20.0;
                }
            }

            // Description match has medium weight
            if (description.Contains(term))
            {
                score += 5.0;
            }

            // Tag match has high weight
            if (tags.Any(tag => tag.Contains(term)))
            {
                score += 8.0;
                // Exact tag match bonus
                if (tags.Contains(term))
                {
                    score += 12.0;
                }
            }

            // Metadata match has low weight
            foreach (KeyValuePair<string, string> meta in item.Metadata)
            {
                if (meta.Value.ToLowerInvariant().Contains(term))
                {
                    score += 2.0;
                }
            }
        }

        return score;
    }

    // TODO: Implement fuzzy matching (Levenshtein distance)
    // TODO: Add support for phrase searching ("exact phrase")
    // TODO: Add support for boolean operators (AND, OR, NOT)
    // TODO: Add support for field-specific searching (title:query)
    // TODO: Integrate with Elasticsearch for production (when server added)
}
