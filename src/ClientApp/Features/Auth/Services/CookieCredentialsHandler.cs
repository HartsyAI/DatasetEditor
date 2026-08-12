using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace DatasetStudio.ClientApp.Features.Auth.Services;

/// <summary>
/// Blazor WASM's HttpClient does not send cookies cross-origin by default -- each
/// HttpRequestMessage must opt in explicitly via SetBrowserRequestCredentials, which maps to
/// the underlying fetch() call's `credentials: 'include'` option. Without this, every
/// authenticated API call would silently 401 even though login itself succeeds (the Set-Cookie
/// half works; this is the "send it back" half). Registered once via AddHttpMessageHandler on
/// each typed HttpClient that talks to the API, rather than set per-call at every call site,
/// so a new API method can't forget it.
/// </summary>
public sealed class CookieCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
