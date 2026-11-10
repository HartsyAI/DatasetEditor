using System.Globalization;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Item versions, for the <c>ETag</c> / <c>If-Match</c> check on item edits (audit E-4). Edits used
/// to be last-writer-wins on the whole item with no way to notice a concurrent change.
/// </summary>
public static class ItemVersions
{
    /// <summary>
    /// A local item's version: its <c>UpdatedAt</c> in ticks. Every edit moves <c>UpdatedAt</c>
    /// forward by at least a millisecond (see <see cref="NextUpdatedAt"/>), so each edit gives a new
    /// version even when two land within the clock's resolution.
    /// </summary>
    public static string ForLocal(DateTime updatedAt) => updatedAt.Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The <c>UpdatedAt</c> to stamp on an edit of an item last updated at <paramref name="previous"/>:
    /// now, to the millisecond, but always at least a millisecond after the previous value. The
    /// millisecond truncation keeps the value exact through storage.
    /// </summary>
    public static DateTime NextUpdatedAt(DateTime previous, DateTime utcNow)
    {
        DateTime now = TruncateToMilliseconds(utcNow);
        DateTime floor = TruncateToMilliseconds(previous).AddMilliseconds(1);
        return new DateTime(Math.Max(now.Ticks, floor.Ticks), DateTimeKind.Utc);
    }

    /// <summary>The <c>ETag</c> header value for a version (a strong entity tag).</summary>
    public static string ToETag(string version) => "\"" + version + "\"";

    /// <summary>
    /// Whether an <c>If-Match</c> header allows an edit of an item at <paramref name="currentVersion"/>.
    /// An absent header allows it (clients that don't send one keep working); <c>*</c> matches any
    /// existing item; otherwise one of the listed tags must equal the version. A weak tag
    /// (<c>W/"…"</c>) is compared by its value, since versions here are only ever strong.
    /// </summary>
    public static bool IfMatchAllows(string? ifMatch, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return true;
        }

        foreach (string candidate in ifMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (candidate == "*")
            {
                return true;
            }

            string tag = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
            if (tag.Length >= 2 && tag[0] == '"' && tag[^1] == '"')
            {
                tag = tag[1..^1];
            }
            if (string.Equals(tag, currentVersion, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }
}
