using System.Globalization;

namespace DatasetStudio.ClientApp.Shared.Presentation;

/// <summary>
/// The one "Updated … ago" wording (DESIGN.md §5.5): just now, N min ago, N h ago, yesterday,
/// then a date such as "12 Nov 2026". The library had its own "N day(s) ago" helper with
/// awkward plurals, and other surfaces printed raw timestamps.
/// </summary>
public static class RelativeTime
{
    /// <summary>Formats a UTC timestamp relative to now.</summary>
    /// <param name="utc">The timestamp, in UTC.</param>
    public static string Format(DateTime utc) => Format(utc, DateTime.UtcNow);

    /// <summary>Formats a UTC timestamp relative to a given "now" (for tests).</summary>
    /// <param name="utc">The timestamp, in UTC.</param>
    /// <param name="nowUtc">The current time, in UTC.</param>
    public static string Format(DateTime utc, DateTime nowUtc)
    {
        TimeSpan age = nowUtc - utc;

        // A timestamp slightly in the future (clock skew between server and browser) reads as now.
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }
        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} min ago";
        }
        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours} h ago";
        }
        if (age < TimeSpan.FromDays(2))
        {
            return "yesterday";
        }
        return utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>The absolute local time for a tooltip next to a relative label.</summary>
    /// <param name="utc">The timestamp, in UTC.</param>
    public static string Absolute(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}
