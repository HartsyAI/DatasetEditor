// Reference/example extension, not a functional feature: the real dataset-items and
// statistics endpoints live in DatasetEndpoints.cs (with real permission checks via
// IDatasetPermissionService). This class exists to demonstrate the extension SDK's endpoint-
// registration shape (BaseApiExtension, IExtensionApiEndpoint, RegisterEndpoints against an
// IEndpointRouteBuilder) -- its two routes intentionally return hardcoded placeholder data and
// carry no auth/permission checks, so do not point real UI at them. See docs/architecture.md
// section 6 for the extension system's actual (mostly unimplemented) state.
//
// This is the API half of the CoreViewer extension.
// Client half is in CoreViewer.Client/CoreViewerClientExtension.cs

using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.Extensions.CoreViewer.Api;

public class CoreViewerApiExtension : BaseApiExtension, IExtensionApiEndpoint
{
    public override ExtensionManifest GetManifest()
    {
        // TODO: Phase 3 - Load from extension.manifest.json
        return new ExtensionManifest
        {
            Metadata = new ExtensionMetadata
            {
                Id = "CoreViewer",
                Name = "Core Dataset Viewer",
                Version = "1.0.0",
                Description = "Basic dataset viewing"
            },
            DeploymentTarget = ExtensionDeploymentTarget.Both
        };
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register API-side services
        // Example: services.AddScoped<IDatasetQueryService, DatasetQueryService>();
        
        base.ConfigureServices(services);
    }

    protected override void OnConfigureApp(IApplicationBuilder app)
    {
        // Register endpoints
        if (app is IEndpointRouteBuilder endpoints)
        {
            RegisterEndpoints(endpoints);
        }
    }

    public string GetBasePath() => "/api/extensions/coreviewer";

    // `new`, not `override`: this implements IExtensionApiEndpoint.RegisterEndpoints (public,
    // called explicitly from OnConfigureApp below), a distinct contract from
    // BaseApiExtension's own protected virtual RegisterEndpoints (which OnConfigureApp
    // deliberately doesn't invoke here).
    public new void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        var basePath = GetBasePath();

        // Placeholder data, not a real query -- see the class-level comment. Use
        // GET /api/datasets/{id}/items for the real, permission-checked equivalent.
        endpoints.MapGet($"{basePath}/datasets/{{datasetId}}/items", async (string datasetId) =>
        {
            return Results.Ok(new { datasetId, items = new[] { "item1", "item2" } });
        });

        // Placeholder data, not a real query. IDatasetItemRepository.GetStatisticsAsync has the
        // real implementation but isn't exposed via any DatasetEndpoints.cs route yet.
        endpoints.MapGet($"{basePath}/datasets/{{datasetId}}/stats", async (string datasetId) =>
        {
            return Results.Ok(new { datasetId, totalItems = 0, size = 0 });
        });
    }

    public IReadOnlyList<ApiEndpointDescriptor> GetEndpointDescriptors()
    {
        return new List<ApiEndpointDescriptor>
        {
            new() { Method = "GET", Route = "/datasets/{datasetId}/items", HandlerType = "CoreViewerApiExtension" },
            new() { Method = "GET", Route = "/datasets/{datasetId}/stats", HandlerType = "CoreViewerApiExtension" }
        };
    }
}
