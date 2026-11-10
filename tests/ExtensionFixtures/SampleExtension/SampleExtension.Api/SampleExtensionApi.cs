using DatasetStudio.Extensions.SDK;
using DatasetStudio.Tests.ExtensionFixtures.SampleExtension.Greeting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.Tests.ExtensionFixtures.SampleExtension;

/// <summary>
/// Test fixture extension. Both methods call into the private SampleExtension.Greeting assembly,
/// so they fail with FileNotFoundException unless the loader resolves the extension's own
/// dependencies (audit X-2).
/// </summary>
public sealed class SampleExtensionApi : IApiExtension
{
    /// <summary>Service key of the greeting registered by <see cref="ConfigureServices"/>.</summary>
    public const string GreetingServiceKey = "SampleExtension.Greeting";

    /// <inheritdoc/>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeyedSingleton(GreetingServiceKey, Greeter.Greet("Dataset Studio"));
    }

    /// <inheritdoc/>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/greeting", () => Results.Text(Greeter.Greet("Dataset Studio")));
    }
}
