namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>Helpers for thumbnail loaders to read an original without unbounded buffering.</summary>
public static class ThumbnailSource
{
    /// <summary>
    /// Reads <paramref name="source"/> into memory, stopping with
    /// <see cref="ThumbnailSourceTooLargeException"/> as soon as it passes
    /// <paramref name="maxBytes"/>. Originals used to be buffered whole whatever their size
    /// (audit S-2, T-3).
    /// </summary>
    public static async Task<byte[]> ReadLimitedAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new ThumbnailSourceTooLargeException(maxBytes);
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
