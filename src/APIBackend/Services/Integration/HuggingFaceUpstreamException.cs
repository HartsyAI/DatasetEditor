using System.Net;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Raised when datasets-server answers with an error status, cannot be reached, or returns a body
/// the client cannot parse. The client used to return null for all of these, which the items
/// endpoint turned into a 200 with no items, so a gated dataset or an outage looked like an empty
/// dataset (audit H-6). Callers that can recover (the row-page cache, the streaming strategy) catch
/// it; everything else lets it reach the endpoint, which maps it to a reason the user can act on.
/// </summary>
public sealed class HuggingFaceUpstreamException : Exception
{
    /// <summary>Creates an upstream failure.</summary>
    /// <param name="message">Human-readable reason, safe to show to the caller.</param>
    /// <param name="statusCode">HTTP status datasets-server returned, or null for transport and parse failures.</param>
    /// <param name="inner">The underlying exception, if any.</param>
    public HuggingFaceUpstreamException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status datasets-server returned, or null when no response was received.</summary>
    public HttpStatusCode? StatusCode { get; }
}
