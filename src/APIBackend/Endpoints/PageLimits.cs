namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>
/// Bounds on client-supplied page sizes. Without a ceiling, `?pageSize=1000000` asks the
/// server to materialise a million rows in one response (audit D-3); the HuggingFace
/// datasets-server rejects anything above 100 with a 422, which surfaces as an empty page.
/// </summary>
internal static class PageLimits
{
    /// <summary>Largest page any list endpoint will serve. Matches the datasets-server maximum.</summary>
    internal const int MaxPageSize = 100;

    /// <summary>Clamps a requested page size into [1, <see cref="MaxPageSize"/>].</summary>
    internal static int ClampPageSize(int requested) => Math.Clamp(requested, 1, MaxPageSize);
}
