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
}

public sealed record ThumbnailResult(ThumbnailOutcome Outcome, byte[]? Bytes);

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
    Task<ThumbnailResult> GetOrCreateJpegAsync(
        string cacheKey,
        int requestedWidth,
        Func<CancellationToken, Task<byte[]?>> loadOriginalAsync,
        CancellationToken cancellationToken);
}
