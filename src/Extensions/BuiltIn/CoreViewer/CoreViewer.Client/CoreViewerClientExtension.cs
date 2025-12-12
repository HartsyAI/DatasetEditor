// TODO: Phase 3 - CoreViewer Client Extension
//
// Purpose: Client-side UI for CoreViewer extension
// Provides Blazor components for dataset viewing
//
// Responsibilities:
// - Render dataset grid view
// - Render dataset list view
// - Render item detail view
// - Handle client-side filtering and sorting
// - Call API endpoints for data
//
// This is the Client half of the CoreViewer extension.
// API half is in CoreViewer.Api/CoreViewerApiExtension.cs

using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.Extensions.CoreViewer.Client;

public class CoreViewerClientExtension : BaseClientExtension
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
        // Register client-side services
        // Example: services.AddScoped<ViewerStateService>();
        
        base.ConfigureServices(services);
    }

    protected override async Task OnInitializeAsync()
    {
        // Initialize client-side resources
        Logger.LogInformation("CoreViewer client initialized");
        await Task.CompletedTask;
    }

    public override void RegisterComponents()
    {
        // TODO: Phase 3 - Register Blazor components
        // Components: GridView, ListView, DetailView, DatasetBrowser
        
        Logger.LogInformation("Registering CoreViewer components");
        base.RegisterComponents();
    }

    public override void RegisterNavigation()
    {
        // TODO: Phase 3 - Register navigation menu items
        // - Browse Datasets (/datasets)
        // - Dataset List (/datasets/list)
        
        Logger.LogInformation("Registering CoreViewer navigation items");
        base.RegisterNavigation();
    }
}
