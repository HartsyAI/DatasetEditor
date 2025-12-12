// TODO: Phase 3 - Client Extension Loader
//
// Called by: ClientExtensionRegistry
// Calls: Assembly.Load(), Type.GetType(), Activator.CreateInstance()
//
// Purpose: Dynamic assembly loading for Blazor WebAssembly extensions
// Similar to ApiExtensionLoader but for client-side (browser) environment.
//
// Key Differences from API Loader:
// - Blazor WASM doesn't support AssemblyLoadContext.Unload() (not collectible)
// - Assemblies must be pre-deployed with the WASM app (in _framework folder)
// - No file system access - assemblies loaded via HTTP
// - Component types must be registered with Blazor's routing system
//
// Responsibilities:
// 1. Load extension assemblies in browser
// 2. Find types implementing IExtension
// 3. Find Blazor component types (types inheriting ComponentBase)
// 4. Instantiate extension classes
// 5. Register component routes dynamically
//
// Blazor WASM Considerations:
// - Assemblies are downloaded as .dll files in _framework folder
// - Assembly.Load() works but loads from pre-downloaded assemblies
// - Hot-reload not supported in WASM (requires app restart)
// - All assemblies must be referenced in project or manually added to publish

using System.Reflection;
using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.ClientApp.Services.Extensions;

/// <summary>
/// Loads extension assemblies in Blazor WebAssembly and creates extension instances.
/// Handles Blazor component discovery and registration.
/// </summary>
public class ClientExtensionLoader
{
    private readonly ILogger _logger;
    private readonly HashSet<Assembly> _loadedAssemblies;

    /// <summary>
    /// Initializes a new client extension loader.
    /// </summary>
    public ClientExtensionLoader(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loadedAssemblies = new HashSet<Assembly>();
    }

    /// <summary>
    /// Loads an extension from its manifest.
    /// </summary>
    /// <param name="manifest">Extension manifest with metadata and paths</param>
    /// <returns>Loaded and instantiated extension</returns>
    public async Task<IExtension> LoadExtensionAsync(ExtensionManifest manifest)
    {
        _logger.LogDebug("Loading extension assembly for: {ExtensionId}", manifest.Metadata.Id);

        // For Client extensions, look for {ExtensionId}.Client.dll
        var assemblyName = $"{manifest.Metadata.Id}.Client";

        _logger.LogDebug("Loading assembly: {AssemblyName}", assemblyName);

        // In Blazor WASM, we use Assembly.Load with the name
        // The assembly must be pre-deployed with the app
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(assemblyName);
            _loadedAssemblies.Add(assembly);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assembly: {AssemblyName}", assemblyName);
            throw new InvalidOperationException(
                $"Extension assembly '{assemblyName}' not found. " +
                $"Ensure the assembly is referenced in the Client project.", ex);
        }

        _logger.LogDebug("Assembly loaded: {AssemblyFullName}", assembly.FullName);

        // Find extension type implementing IExtension
        var extensionType = FindExtensionType(assembly);

        if (extensionType == null)
        {
            throw new InvalidOperationException(
                $"No type implementing IExtension found in {assemblyName}");
        }

        _logger.LogDebug("Found extension type: {TypeName}", extensionType.FullName);

        // Create extension instance
        var extension = Activator.CreateInstance(extensionType) as IExtension;

        if (extension == null)
        {
            throw new InvalidOperationException(
                $"Failed to create instance of {extensionType.FullName}");
        }

        // Discover Blazor components in the assembly
        await DiscoverComponentsAsync(assembly, manifest);

        _logger.LogInformation(
            "Extension loaded successfully: {ExtensionId} from {AssemblyName}",
            manifest.Metadata.Id,
            assemblyName);

        return extension;
    }

    /// <summary>
    /// Finds the type implementing IExtension in the assembly.
    /// </summary>
    private Type? FindExtensionType(Assembly assembly)
    {
        try
        {
            var extensionTypes = assembly.GetTypes()
                .Where(t => typeof(IExtension).IsAssignableFrom(t) &&
                           !t.IsInterface &&
                           !t.IsAbstract)
                .ToList();

            if (extensionTypes.Count == 0)
            {
                _logger.LogWarning("No IExtension implementation found in {Assembly}", assembly.FullName);
                return null;
            }

            if (extensionTypes.Count > 1)
            {
                _logger.LogWarning(
                    "Multiple IExtension implementations found in {Assembly}, using first: {Type}",
                    assembly.FullName,
                    extensionTypes[0].FullName);
            }

            return extensionTypes[0];
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogError(ex, "Failed to load types from assembly {Assembly}", assembly.FullName);
            foreach (var loaderEx in ex.LoaderExceptions)
            {
                _logger.LogError(loaderEx, "Loader exception");
            }
            throw;
        }
    }

    /// <summary>
    /// Discovers Blazor components in the extension assembly.
    /// Finds all types inheriting from ComponentBase.
    /// </summary>
    private async Task DiscoverComponentsAsync(Assembly assembly, ExtensionManifest manifest)
    {
        _logger.LogDebug("Discovering Blazor components in {Assembly}", assembly.FullName);

        try
        {
            var componentTypes = assembly.GetTypes()
                .Where(t => typeof(ComponentBase).IsAssignableFrom(t) &&
                           !t.IsAbstract &&
                           t.IsPublic)
                .ToList();

            _logger.LogInformation(
                "Found {Count} Blazor components in {ExtensionId}",
                componentTypes.Count,
                manifest.Metadata.Id);

            // TODO: Phase 3 - Register components with Blazor routing
            // For each component:
            // 1. Check for [Route] attribute
            // 2. Register route with Blazor router
            // 3. Add to manifest.BlazorComponents dictionary

            foreach (var componentType in componentTypes)
            {
                _logger.LogDebug("Discovered component: {ComponentType}", componentType.FullName);

                // Check for Route attribute
                var routeAttr = componentType.GetCustomAttribute<RouteAttribute>();
                if (routeAttr != null)
                {
                    _logger.LogDebug(
                        "Component {ComponentType} has route: {Route}",
                        componentType.Name,
                        routeAttr.Template);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering components in {Assembly}", assembly.FullName);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets all loaded assemblies.
    /// </summary>
    public IReadOnlySet<Assembly> GetLoadedAssemblies()
    {
        return _loadedAssemblies;
    }

    /// <summary>
    /// Gets all Blazor component types from loaded extensions.
    /// </summary>
    public IEnumerable<Type> GetAllComponentTypes()
    {
        return _loadedAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ComponentBase).IsAssignableFrom(t) &&
                       !t.IsAbstract &&
                       t.IsPublic);
    }

    /// <summary>
    /// Gets component types with specific route patterns.
    /// Useful for generating navigation menus.
    /// </summary>
    public IEnumerable<(Type Type, RouteAttribute Route)> GetRoutedComponents()
    {
        return GetAllComponentTypes()
            .Select(t => (Type: t, Route: t.GetCustomAttribute<RouteAttribute>()))
            .Where(x => x.Route != null)!;
    }
}
