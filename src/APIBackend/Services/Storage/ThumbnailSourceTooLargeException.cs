namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>An original is too big to thumbnail; <see cref="ThumbnailService"/> answers <see cref="ThumbnailOutcome.TooLarge"/>.</summary>
public sealed class ThumbnailSourceTooLargeException : Exception
{
    /// <summary>Creates the exception for the limit that was passed.</summary>
    public ThumbnailSourceTooLargeException(long maxBytes)
        : base($"The original is larger than the {maxBytes} byte thumbnail source limit.")
    {
    }
}
