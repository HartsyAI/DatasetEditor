// TODO: Phase 3 - CoreViewer API Extension
//
// Purpose: API-side logic for CoreViewer extension
// Provides backend endpoints for dataset viewing operations
//
// Responsibilities:
// - Expose REST endpoints for dataset queries
// - Handle pagination and filtering
// - Generate dataset statistics
// - Optimize data retrieval for large datasets
//
// This is the API half of the CoreViewer extension.
// Client half is in CoreViewer.Client/CoreViewerClientExtension.cs

using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Builder;
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

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        var basePath = GetBasePath();

        // GET /api/extensions/coreviewer/datasets/{datasetId}/items
        endpoints.MapGet($"{basePath}/datasets/{{datasetId}}/items", async (string datasetId) =>
        {
            // TODO: Phase 3 - Implement dataset items query with pagination
            return Results.Ok(new { datasetId, items = new[] { "item1", "item2" } });
        });

        // GET /api/extensions/coreviewer/datasets/{datasetId}/stats
        endpoints.MapGet($"{basePath}/datasets/{{datasetId}}/stats", async (string datasetId) =>
        {
            // TODO: Phase 3 - Implement dataset statistics
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
