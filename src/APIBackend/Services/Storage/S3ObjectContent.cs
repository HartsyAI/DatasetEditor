namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// An open S3 object: its body stream and the size S3 reported for it. Disposing it disposes the
/// underlying SDK response, not just its stream. GetObjectStreamAsync used to hand back only
/// <c>ResponseStream</c> and never dispose the <c>GetObjectResponse</c> that owned it (audit S-4).
/// </summary>
public sealed class S3ObjectContent : IDisposable, IAsyncDisposable
{
    private readonly IDisposable _owner;

    /// <summary>Wraps a body stream and the object that owns it (the SDK response).</summary>
    public S3ObjectContent(Stream content, long contentLength, IDisposable owner)
    {
        Content = content;
        ContentLength = contentLength;
        _owner = owner;
    }

    /// <summary>The object's body.</summary>
    public Stream Content { get; }

    /// <summary>The object's size in bytes, from the response headers.</summary>
    public long ContentLength { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Content.Dispose();
        _owner.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        _owner.Dispose();
    }
}
