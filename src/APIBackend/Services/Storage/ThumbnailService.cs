using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Disk-backed thumbnail cache. Decode/resize/encode is real CPU work, so a burst of grid
/// requests against a cold cache (the exact shape of a freshly-scrolled virtualized grid --
/// see docs/verification/phase-4/README.md) is bounded by a small global semaphore rather than
/// running every request's resize fully in parallel.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private const int MinWidth = 32;
    private const int MaxWidth = 1024;
    private const int JpegQuality = 80;

    private readonly string _cacheRoot;
    private readonly ILogger<ThumbnailService> _logger;

    // Bounds concurrent decode/resize/encode work across ALL requests -- separate from
    // per-key locking below, which only dedupes repeat requests for the identical thumbnail.
    private readonly SemaphoreSlim _generationGate;

    // Prevents two concurrent requests for the exact same (cacheKey, width) from both
    // decoding/resizing/writing the same file; low cardinality in practice (grid pagination
    // rarely re-requests an in-flight item), so a plain dictionary of per-key gates is fine --
    // no eviction needed, entries are cheap and the process is not long enough-lived at scale
    // for this to matter.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKeyGates = new();

    public ThumbnailService(string thumbnailPath, ILogger<ThumbnailService> logger)
    {
        _cacheRoot = Path.GetFullPath(thumbnailPath);
        Directory.CreateDirectory(_cacheRoot);
        _logger = logger;
        _generationGate = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));
    }

    public async Task<ThumbnailResult> GetOrCreateJpegAsync(
        string cacheKey,
        int requestedWidth,
        Func<CancellationToken, Task<byte[]?>> loadOriginalAsync,
        CancellationToken cancellationToken)
    {
        int width = Math.Clamp(requestedWidth, MinWidth, MaxWidth);
        string cachePath = BuildCachePath(cacheKey, width);

        if (File.Exists(cachePath))
        {
            byte[] cached = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            return new ThumbnailResult(ThumbnailOutcome.Generated, cached);
        }

        SemaphoreSlim keyGate = _perKeyGates.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await keyGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check: another request may have finished generating this exact thumbnail
            // while we were waiting on the gate.
            if (File.Exists(cachePath))
            {
                byte[] cached = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                return new ThumbnailResult(ThumbnailOutcome.Generated, cached);
            }

            byte[]? original = await loadOriginalAsync(cancellationToken);
            if (original is null)
            {
                return new ThumbnailResult(ThumbnailOutcome.SourceNotFound, null);
            }

            await _generationGate.WaitAsync(cancellationToken);
            try
            {
                byte[]? resized = Resize(original, width);
                if (resized is null)
                {
                    // Not cached -- an undecodable file should still let ImageCard fall back
                    // to the original on future requests rather than being permanently stuck
                    // (rare path in practice: only reachable for a non-image file that somehow
                    // has a dataset item's ImageUrl pointing at it).
                    return new ThumbnailResult(ThumbnailOutcome.Undecodable, original);
                }

                await WriteAtomicallyAsync(cachePath, resized, cancellationToken);
                return new ThumbnailResult(ThumbnailOutcome.Generated, resized);
            }
            finally
            {
                _generationGate.Release();
            }
        }
        finally
        {
            keyGate.Release();
            // Best-effort cleanup so _perKeyGates doesn't grow forever across a long-running
            // process; a race here (another waiter grabs it between Release and TryRemove) just
            // means that waiter's gate object gets orphaned after use, which is harmless.
            if (keyGate.CurrentCount == 1)
            {
                _perKeyGates.TryRemove(cachePath, out _);
            }
        }
    }

    private string BuildCachePath(string cacheKey, int width)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        // Two-level fan-out (first 2 hex chars as a subdirectory) keeps any single directory
        // from accumulating tens of thousands of entries at real dataset scale.
        string subdir = Path.Combine(_cacheRoot, hash[..2]);
        Directory.CreateDirectory(subdir);
        return Path.Combine(subdir, $"{hash}-{width}.jpg");
    }

    private byte[]? Resize(byte[] original, int width)
    {
        try
        {
            using SKBitmap? decoded = SKBitmap.Decode(original);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return null;
            }

            // Never upscale -- a source narrower than the requested width is served at its
            // own native size.
            int targetWidth = Math.Min(width, decoded.Width);
            int targetHeight = (int)Math.Round(decoded.Height * (targetWidth / (double)decoded.Width));
            targetHeight = Math.Max(1, targetHeight);

            using SKBitmap? resizedBitmap = targetWidth == decoded.Width
                ? null
                : decoded.Resize(
                    new SKImageInfo(targetWidth, targetHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            using SKImage image = SKImage.FromBitmap(resizedBitmap ?? decoded);
            // Always JPEG regardless of source format -- simplest single content-type for the
            // whole cache; drops alpha on PNGs, an accepted tradeoff for grid thumbnails (see
            // docs/verification/phase-4/README.md scope decisions).
            using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return encoded.ToArray();
        }
        catch (Exception ex)
        {
            // SkiaSharp throws on some malformed inputs rather than returning null from
            // Decode -- treat any decode/resize/encode failure as "undecodable", not a 500.
            _logger.LogWarning(ex, "Thumbnail generation failed, falling back to original bytes");
            return null;
        }
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        // Write-then-rename so a concurrent reader of `path` (a different process, or a
        // request that lost the File.Exists race by microseconds) never sees a partially
        // written file.
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
