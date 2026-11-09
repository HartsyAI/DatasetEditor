using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Disk-backed thumbnail cache. Decode/resize/encode is real CPU work, so a burst of grid
/// requests against a cold cache (the exact shape of a freshly-scrolled virtualized grid --
/// see docs/verification/phase-4/README.md) is bounded by a small global semaphore rather than
/// running every request's resize fully in parallel. The cache is capped by total size
/// (<see cref="ThumbnailOptions.MaxCacheBytes"/>), oldest first, and originals are size- and
/// pixel-checked before decoding.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private const int MinWidth = 32;
    private const int MaxWidth = 1024;
    private const int JpegQuality = 80;

    /// <summary>A cache hit refreshes the file's age at most this often, so reads stay cheap.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);

    private readonly string _cacheRoot;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly ThumbnailOptions _options;

    // Bounds concurrent decode/resize/encode work across ALL requests -- separate from
    // per-key locking below, which only dedupes repeat requests for the identical thumbnail.
    private readonly SemaphoreSlim _generationGate;

    // Prevents two concurrent requests for the exact same (cacheKey, width) from both
    // decoding/resizing/writing the same file.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKeyGates = new();

    private int _writesSinceSweep;
    private int _sweeping;

    public ThumbnailService(string thumbnailPath, ILogger<ThumbnailService> logger)
        : this(thumbnailPath, logger, new ThumbnailOptions())
    {
    }

    public ThumbnailService(string thumbnailPath, ILogger<ThumbnailService> logger, ThumbnailOptions options)
    {
        _cacheRoot = Path.GetFullPath(thumbnailPath);
        Directory.CreateDirectory(_cacheRoot);
        _logger = logger;
        _options = options;
        _generationGate = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));

        // The cache had no size limit at all (audit T-1); bring an existing one under the cap.
        TriggerSweep();
    }

    /// <inheritdoc />
    public long MaxSourceBytes => _options.MaxSourceBytes;

    public async Task<ThumbnailResult> GetOrCreateJpegAsync(
        string cacheKey,
        int requestedWidth,
        Func<CancellationToken, Task<byte[]?>> loadOriginalAsync,
        CancellationToken cancellationToken)
    {
        int width = Math.Clamp(requestedWidth, MinWidth, MaxWidth);
        string cachePath = BuildCachePath(cacheKey, width);

        if (await TryReadCachedAsync(cachePath, cancellationToken) is { } hit)
        {
            return new ThumbnailResult(ThumbnailOutcome.Generated, hit, width);
        }

        SemaphoreSlim keyGate = _perKeyGates.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await keyGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check: another request may have finished generating this exact thumbnail
            // while we were waiting on the gate.
            if (await TryReadCachedAsync(cachePath, cancellationToken) is { } raced)
            {
                return new ThumbnailResult(ThumbnailOutcome.Generated, raced, width);
            }

            byte[]? original;
            try
            {
                original = await loadOriginalAsync(cancellationToken);
            }
            catch (ThumbnailSourceTooLargeException)
            {
                return new ThumbnailResult(ThumbnailOutcome.TooLarge, null, width);
            }
            if (original is null)
            {
                return new ThumbnailResult(ThumbnailOutcome.SourceNotFound, null, width);
            }
            if (original.LongLength > _options.MaxSourceBytes)
            {
                return new ThumbnailResult(ThumbnailOutcome.TooLarge, null, width);
            }

            await _generationGate.WaitAsync(cancellationToken);
            try
            {
                ResizeResult resized = Resize(original, width);
                if (resized.TooManyPixels)
                {
                    return new ThumbnailResult(ThumbnailOutcome.TooLarge, null, width);
                }
                if (resized.Jpeg is null)
                {
                    // Not cached -- an undecodable file should still let ImageCard fall back
                    // to the original on future requests rather than being permanently stuck.
                    return new ThumbnailResult(ThumbnailOutcome.Undecodable, original, width);
                }

                await WriteAtomicallyAsync(cachePath, resized.Jpeg, cancellationToken);
                if (Interlocked.Increment(ref _writesSinceSweep) >= _options.SweepEveryWrites)
                {
                    TriggerSweep();
                }
                return new ThumbnailResult(ThumbnailOutcome.Generated, resized.Jpeg, width);
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

    /// <summary>
    /// Deletes the oldest cached thumbnails until the cache is at most 90% of
    /// <see cref="ThumbnailOptions.MaxCacheBytes"/>, and removes temp files older than an hour.
    /// "Oldest" is by write time, which cache hits refresh (at most hourly), so frequently viewed
    /// thumbnails survive.
    /// </summary>
    public Task SweepAsync()
    {
        DirectoryInfo root = new(_cacheRoot);
        if (!root.Exists)
        {
            return Task.CompletedTask;
        }

        List<FileInfo> files = new();
        long total = 0;
        DateTime staleTemp = DateTime.UtcNow.AddHours(-1);
        foreach (FileInfo file in root.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                if (file.LastWriteTimeUtc < staleTemp)
                {
                    TryDelete(file);
                }
                continue;
            }
            files.Add(file);
            total += file.Length;
        }

        if (total <= _options.MaxCacheBytes)
        {
            return Task.CompletedTask;
        }

        long target = (long)(_options.MaxCacheBytes * 0.9);
        int deleted = 0;
        foreach (FileInfo file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= target)
            {
                break;
            }
            long length = file.Length;
            if (TryDelete(file))
            {
                total -= length;
                deleted++;
            }
        }
        _logger.LogInformation("Thumbnail cache sweep removed {Count} file(s); {Bytes} bytes remain", deleted, total);
        return Task.CompletedTask;
    }

    private void TriggerSweep()
    {
        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
        {
            return;
        }
        Interlocked.Exchange(ref _writesSinceSweep, 0);
        _ = Task.Run(async () =>
        {
            try
            {
                await SweepAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail cache sweep failed");
            }
            finally
            {
                Interlocked.Exchange(ref _sweeping, 0);
            }
        });
    }

    /// <summary>
    /// Reads a cached thumbnail, or null if it isn't there (including when a sweep deleted it
    /// between the existence check and the read).
    /// </summary>
    private static async Task<byte[]?> TryReadCachedAsync(string cachePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }
            byte[] bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (now - File.GetLastWriteTimeUtc(cachePath) > TouchInterval)
            {
                File.SetLastWriteTimeUtc(cachePath, now);
            }
            return bytes;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
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

    private sealed record ResizeResult(byte[]? Jpeg, bool TooManyPixels);

    private ResizeResult Resize(byte[] original, int width)
    {
        try
        {
            using SKData data = SKData.CreateCopy(original);
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return new ResizeResult(null, false);
            }

            // Refuse from the header, before any pixel memory is allocated (audit T-3).
            if ((long)codec.Info.Width * codec.Info.Height > _options.MaxSourcePixels)
            {
                return new ResizeResult(null, true);
            }

            // Let the codec decode at a reduced scale where it can (JPEG decodes at 1/2, 1/4 or
            // 1/8 almost for free), never below the target width, so a large photo isn't fully
            // expanded just to be shrunk.
            int targetWidth = Math.Min(width, codec.Info.Width);
            SKSizeI decodeSize = codec.GetScaledDimensions((float)targetWidth / codec.Info.Width);
            if (decodeSize.Width < targetWidth)
            {
                decodeSize = new SKSizeI(codec.Info.Width, codec.Info.Height);
            }

            SKImageInfo decodeInfo = new(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKBitmap decoded = new(decodeInfo);
            SKCodecResult result = codec.GetPixels(decodeInfo, decoded.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return new ResizeResult(null, false);
            }

            int targetHeight = Math.Max(1, (int)Math.Round(codec.Info.Height * (targetWidth / (double)codec.Info.Width)));
            using SKBitmap? resizedBitmap = decoded.Width == targetWidth && decoded.Height == targetHeight
                ? null
                : decoded.Resize(
                    new SKImageInfo(targetWidth, targetHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            using SKImage image = SKImage.FromBitmap(resizedBitmap ?? decoded);
            // Always JPEG regardless of source format -- simplest single content-type for the
            // whole cache; drops alpha on PNGs, an accepted tradeoff for grid thumbnails.
            using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return new ResizeResult(encoded.ToArray(), false);
        }
        catch (Exception ex)
        {
            // SkiaSharp throws on some malformed inputs rather than returning null -- treat any
            // decode/resize/encode failure as "undecodable", not a 500.
            _logger.LogWarning(ex, "Thumbnail generation failed, falling back to original bytes");
            return new ResizeResult(null, false);
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

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
