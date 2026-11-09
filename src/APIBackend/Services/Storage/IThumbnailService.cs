namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>What GetOrCreateJpegAsync managed to do for a requested (cacheKey, width) pair.</summary>
public enum ThumbnailOutcome
{
    /// <summary><c>loadOriginalAsync</c> returned null -- the source bytes don't exist. Caller should 404.</summary>
    SourceNotFound,

    /// <summary>
    /// Original bytes were loaded but SkiaSharp couldn't decode them as an image (corrupt file,
    /// unsupported format). <see cref="ThumbnailResult.Bytes"/> holds the original, unresized
    /// bytes so the caller can fall back to serving them as-is rather than erroring.
    /// </summary>
    Undecodable,

    /// <summary>A resized JPEG was generated (or served from cache). <see cref="ThumbnailResult.Bytes"/> holds it.</summary>
    Generated,

    /// <summary>
    /// The original is bigger than <see cref="ThumbnailOptions.MaxSourceBytes"/> or
    /// <see cref="ThumbnailOptions.MaxSourcePixels"/>, so no thumbnail is made. No bytes.
    /// </summary>
    TooLarge,
}

/// <summary>The result of a thumbnail request.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Bytes">The JPEG (Generated) or the original (Undecodable).</param>
/// <param name="Width">The width the request was served at after clamping, so callers can report it.</param>
public sealed record ThumbnailResult(ThumbnailOutcome Outcome, byte[]? Bytes, int Width = 0);

/// <summary>
/// Generic (cacheKey, width) -&gt; resized-JPEG-bytes cache backed by disk, shared by every
/// thumbnail-serving route regardless of where the original bytes come from (local disk,
/// an outbound HTTP fetch, ...). Callers own resolving the original bytes and choosing a
/// cacheKey that's stable across requests but changes whenever the underlying image could
/// (see the hf-thumbnail cache key, which folds in repository/config/split precisely because
/// those can change on an existing dataset row).
/// </summary>
public interface IThumbnailService
{
    /// <summary>Largest original a loader should read; <see cref="ThumbnailSource.ReadLimitedAsync"/> enforces it.</summary>
    long MaxSourceBytes { get; }

    Task<ThumbnailResult> GetOrCreateJpegAsync(
        string cacheKey,
        int requestedWidth,
        Func<CancellationToken, Task<byte[]?>> loadOriginalAsync,
        CancellationToken cancellationToken);
}
