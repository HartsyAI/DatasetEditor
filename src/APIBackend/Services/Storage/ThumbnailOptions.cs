namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Limits for thumbnail generation and the on-disk thumbnail cache (configuration section
/// <c>Thumbnails</c>). The cache used to grow without bound, and originals of any size were read
/// fully into memory and decoded without a pixel check (audit T-1, T-3, S-2).
/// </summary>
public sealed class ThumbnailOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Thumbnails";

    /// <summary>Largest total size of cached thumbnails; the oldest are deleted past it.</summary>
    public long MaxCacheBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Largest original read to make a thumbnail. Bigger originals aren't thumbnailed.</summary>
    public long MaxSourceBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Largest original, in pixels, decoded to make a thumbnail. Checked from the image header
    /// before decoding, so a small file that claims enormous dimensions (a decompression bomb)
    /// is refused instead of allocating gigabytes.
    /// </summary>
    public long MaxSourcePixels { get; set; } = 100_000_000;

    /// <summary>Cache writes between size sweeps (a sweep also runs at startup).</summary>
    public int SweepEveryWrites { get; set; } = 500;
}
