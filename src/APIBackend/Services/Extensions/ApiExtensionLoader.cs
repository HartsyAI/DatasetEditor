// TODO: Phase 3 - API Extension Loader
//
// Called by: ApiExtensionRegistry
// Calls: Assembly.LoadFrom(), Activator.CreateInstance(), Type.GetType()
//
// Purpose: Dynamic assembly loading and extension instantiation
// Handles the low-level mechanics of loading extension DLLs and creating instances.
//
// Responsibilities:
// 1. Load extension assemblies using AssemblyLoadContext
// 2. Find types implementing IExtension in the assembly
// 3. Instantiate extension classes
// 4. Handle assembly isolation (for future hot-reload support)
// 5. Manage assembly dependencies
// 6. Detect version conflicts
//
// Key Design Decisions:
// - Uses AssemblyLoadContext for isolation (allows unloading in future)
// - Scans assembly for types implementing IExtension
// - Supports both API and "Both" deployment targets
// - Validates extension compatibility before loading
//
// Security Considerations:
// - Only load from trusted directories (built-in and user extensions)
// - Validate assembly signatures (TODO: Phase 4)
// - Sandbox extension code (TODO: Phase 4)
//
// Future Enhancements:
// - Hot-reload support (unload/reload assemblies)
// - Assembly caching
// - Multi-version support (side-by-side loading)

using System.Reflection;
using System.Runtime.Loader;
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// Loads extension assemblies and creates extension instances.
/// Handles dynamic assembly loading with isolation support.
/// </summary>
public class ApiExtensionLoader
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, AssemblyLoadContext> _loadContexts;

    /// <summary>
    /// Initializes a new extension loader.
    /// </summary>
    public ApiExtensionLoader(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loadContexts = new Dictionary<string, AssemblyLoadContext>();
    }

    /// <summary>
    /// Loads an extension from its manifest.
    /// </summary>
    /// <param name="manifest">Extension manifest with metadata and paths</param>
    /// <returns>Loaded and instantiated extension</returns>
    public async Task<IExtension> LoadExtensionAsync(ExtensionManifest manifest)
    {
        if (manifest.DirectoryPath == null)
        {
            throw new InvalidOperationException($"Extension {manifest.Metadata.Id} has no directory path");
        }

        _logger.LogDebug("Loading extension assembly for: {ExtensionId}", manifest.Metadata.Id);

        // Construct assembly path
        // For API extensions, look for {ExtensionId}.Api.dll
        var assemblyName = $"{manifest.Metadata.Id}.Api.dll";
        var assemblyPath = Path.Combine(manifest.DirectoryPath, assemblyName);

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Extension assembly not found: {assemblyPath}");
        }

        _logger.LogDebug("Loading assembly: {AssemblyPath}", assemblyPath);

        // Create isolated load context for this extension
        var loadContext = new ExtensionLoadContext(assemblyPath, manifest.Metadata.Id);
        _loadContexts[manifest.Metadata.Id] = loadContext;

        // Load the assembly
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        _logger.LogDebug("Assembly loaded: {AssemblyName}", assembly.FullName);

        // Find extension type implementing IExtension
        var extensionType = FindExtensionType(assembly);

        if (extensionType == null)
        {
            throw new InvalidOperationException(
                $"No type implementing IExtension found in {assemblyPath}");
        }

        _logger.LogDebug("Found extension type: {TypeName}", extensionType.FullName);

        // Create extension instance
        var extension = Activator.CreateInstance(extensionType) as IExtension;

        if (extension == null)
        {
            throw new InvalidOperationException(
                $"Failed to create instance of {extensionType.FullName}");
        }

        _logger.LogInformation(
            "Extension loaded successfully: {ExtensionId} from {AssemblyPath}",
            manifest.Metadata.Id,
            assemblyPath);

        return await Task.FromResult(extension);
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
    /// Unloads an extension (for future hot-reload support).
    /// </summary>
    public void UnloadExtension(string extensionId)
    {
        if (_loadContexts.TryGetValue(extensionId, out var loadContext))
        {
            _logger.LogInformation("Unloading extension: {ExtensionId}", extensionId);

            loadContext.Unload();
            _loadContexts.Remove(extensionId);
        }
    }
}

/// <summary>
/// Isolated assembly load context for extensions.
/// Allows unloading extensions for hot-reload scenarios.
/// </summary>
internal class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _extensionId;

    public ExtensionLoadContext(string assemblyPath, string extensionId)
        : base(name: $"Extension_{extensionId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _extensionId = extensionId;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve dependency
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Let the default context handle it (for shared dependencies)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
