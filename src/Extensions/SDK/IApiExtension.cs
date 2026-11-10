using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// The contract an API extension implements. The API creates one instance per extension through
/// its public parameterless constructor, calls <see cref="ConfigureServices"/> before the host is
/// built and <see cref="MapEndpoints"/> after it is built.
/// </summary>
/// <remarks>
/// An extension runs inside the API process with the same privileges as the API itself. Nothing
/// sandboxes it, and the API does not check signatures, so only install extensions you trust
/// (audit X-3; see docs/DEPLOYMENT.md).
/// </remarks>
public interface IApiExtension
{
    /// <summary>
    /// Registers the extension's services. Runs before the host is built, so no service provider
    /// exists yet: read settings from <paramref name="configuration"/> (by convention the section
    /// <c>Extensions:{Id}</c>) and defer anything that needs other services to a hosted service or
    /// to the endpoint handlers.
    /// </summary>
    /// <param name="services">The API's service collection.</param>
    /// <param name="configuration">The API's full configuration.</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps the extension's endpoints. <paramref name="endpoints"/> is a route group at
    /// <c>/api/extensions/{id}</c> (the manifest id in lower case) that already requires an
    /// authenticated user. The group cannot check dataset permissions, so an extension must not
    /// return or change dataset data until the SDK exposes a permission check.
    /// </summary>
    /// <param name="endpoints">The extension's route group.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
