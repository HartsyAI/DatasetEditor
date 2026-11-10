using DatasetStudio.DTO.Items;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// What a bulk edit did, with a sentence for the user. The client used to show only the server's
/// updated count, so items that were denied or missing silently made the number smaller (audit E-1).
/// </summary>
/// <param name="Succeeded">False when the request itself failed (nothing was edited).</param>
/// <param name="UpdatedCount">How many items were edited.</param>
/// <param name="Skipped">The items left unchanged, with the server's reasons.</param>
/// <param name="Summary">What happened, in words: "Updated 8 items. 2 couldn't be edited: …".</param>
public sealed record BulkEditResult(bool Succeeded, int UpdatedCount, IReadOnlyList<BulkSkippedItem> Skipped, string Summary)
{
    /// <summary>The result of a request that failed outright.</summary>
    public static BulkEditResult Failed(int requested)
    {
        return new BulkEditResult(false, 0, Array.Empty<BulkSkippedItem>(), $"Couldn't edit the {Items(requested)}. Try again.");
    }

    /// <summary>The result of the server's report.</summary>
    public static BulkEditResult From(BulkUpdateItemsResponse response, int requested)
    {
        return new BulkEditResult(true, response.UpdatedCount, response.Skipped, Describe(response, requested));
    }

    /// <summary>
    /// "Updated 3 items." when nothing was skipped; otherwise also how many couldn't be edited and
    /// why, e.g. "Updated 3 of 5 items. 2 couldn't be edited: 1 not found, 1 not yours to edit."
    /// </summary>
    public static string Describe(BulkUpdateItemsResponse response, int requested)
    {
        if (response.Skipped.Count == 0)
        {
            return $"Updated {Items(response.UpdatedCount)}.";
        }

        IEnumerable<string> reasons = response.Skipped
            .GroupBy(s => s.Reason)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {ReasonText(g.Key)}");
        string couldNot = response.Skipped.Count == 1 ? "1 couldn't be edited" : $"{response.Skipped.Count} couldn't be edited";
        return $"Updated {response.UpdatedCount} of {Items(requested)}. {couldNot}: {string.Join(", ", reasons)}.";
    }

    private static string ReasonText(string reason)
    {
        return reason switch
        {
            BulkSkipReasons.NotFound => "not found",
            BulkSkipReasons.Forbidden => "not yours to edit",
            BulkSkipReasons.TagsUnknown => "tags not loaded",
            _ => reason
        };
    }

    private static string Items(int count) => count == 1 ? "1 item" : $"{count} items";
}
