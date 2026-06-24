using Microsoft.AspNetCore.Builder;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Contract for API-side extensions. Extends <see cref="IExtension"/> with the
/// ASP.NET Core middleware/endpoint hook, which is only available on the API host.
///
/// This deliberately lives in a separate assembly (Extensions.SDK.Api) so the core
/// <see cref="IExtension"/> contract stays free of the ASP.NET Core shared framework
/// and can be referenced by the Blazor WebAssembly client.
/// </summary>
public interface IApiExtension : IExtension
{
    /// <summary>
    /// Configures the application middleware pipeline and registers endpoints.
    /// Called after services are configured but before the app runs.
    /// </summary>
    /// <param name="app">Application builder to configure middleware.</param>
    void ConfigureApp(IApplicationBuilder app);
}
