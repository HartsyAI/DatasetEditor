// Reference/example extension, not a functional feature -- and currently unreachable at
// runtime: the only thing that ever called extensions like this, ClientExtensionRegistry, was
// removed in Phase 7 because it scanned the real filesystem (Directory.Exists/File.Exists),
// which doesn't work in a Blazor WebAssembly browser sandbox and had zero call sites of its
// own. RegisterComponents/RegisterNavigation below are no-ops (they only log), so this class
// was already honest about doing nothing -- it's kept building (see DatasetStudio.sln) so it
// still compile-checks against the extension SDK, not as a working feature.
//
// This is the Client half of the CoreViewer extension.
// API half is in CoreViewer.Api/CoreViewerApiExtension.cs

using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
