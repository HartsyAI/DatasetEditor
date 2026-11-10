using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.Extensions.CoreViewer.Api;

/// <summary>
/// The built-in reference extension. It shows the smallest working shape of an API extension and
/// proves the loader finds and runs one under <c>dotnet run</c> and in the Docker image. It
/// registers no services and maps no endpoints.
/// </summary>
/// <remarks>
/// It used to map two routes under <c>/api/extensions/coreviewer/datasets/{id}</c> that returned
/// hard-coded placeholder data without authentication or a dataset permission check (audit X-5).
/// They were removed rather than secured because they did nothing real, and an extension cannot
/// make the per-dataset permission decision AGENTS.md requires: <c>IDatasetPermissionService</c>
/// lives in the API, not in the SDK.
/// </remarks>
public sealed class CoreViewerApiExtension : IApiExtension
{
    /// <inheritdoc/>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <inheritdoc/>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
