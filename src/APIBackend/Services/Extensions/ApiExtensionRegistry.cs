using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// Manages discovery, loading, and lifecycle of API-side extensions.
/// Scans Extensions/BuiltIn and Extensions/Community directories for extensions.
/// </summary>
public class ApiExtensionRegistry
{
    private readonly ILogger<ApiExtensionRegistry> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, LoadedExtension> _loadedExtensions = new();
    private readonly string _builtInExtensionsPath;
    private readonly string _communityExtensionsPath;

    public ApiExtensionRegistry(
        ILogger<ApiExtensionRegistry> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        var basePath = Directory.GetCurrentDirectory();
        _builtInExtensionsPath = Path.Combine(basePath, "Extensions", "BuiltIn");
        _communityExtensionsPath = Path.Combine(basePath, "Extensions", "Community");
    }

    /// <summary>
    /// Discovers and loads all available extensions.
    /// </summary>
    public async Task<List<IExtension>> DiscoverAndLoadAsync()
    {
        _logger.LogInformation("Discovering API extensions...");

        var manifests = new List<(ExtensionManifest Manifest, string Directory)>();

        // Scan BuiltIn extensions
        if (Directory.Exists(_builtInExtensionsPath))
        {
            manifests.AddRange(await ScanDirectoryForManifestsAsync(_builtInExtensionsPath));
            _logger.LogInformation("Found {Count} built-in extension(s)", manifests.Count);
        }

        // Scan Community extensions
        if (Directory.Exists(_communityExtensionsPath))
        {
            var communityCount = manifests.Count;
            manifests.AddRange(await ScanDirectoryForManifestsAsync(_communityExtensionsPath));
            _logger.LogInformation("Found {Count} community extension(s)", manifests.Count - communityCount);
        }

        // Filter by deployment target
        manifests = manifests
            .Where(m => m.Manifest.DeploymentTarget == ExtensionDeploymentTarget.Api ||
                       m.Manifest.DeploymentTarget == ExtensionDeploymentTarget.Both)
            .ToList();

        _logger.LogInformation("Total API extensions to load: {Count}", manifests.Count);

        // Check for disabled extensions
        var disabledExtensions = _configuration.GetSection("Extensions:DisabledExtensions")
            .Get<List<string>>() ?? new List<string>();

        manifests = manifests
            .Where(m => !disabledExtensions.Contains(m.Manifest.Metadata.Id))
            .ToList();

        if (disabledExtensions.Any())
        {
            _logger.LogInformation("Disabled extensions: {Extensions}", string.Join(", ", disabledExtensions));
        }

        // Resolve dependencies and sort
        manifests = await ResolveDependenciesAsync(manifests);

        // Load extensions
        var loadedExtensions = new List<IExtension>();
        foreach (var (manifest, directory) in manifests)
        {
            try
            {
                var extension = await LoadExtensionAsync(manifest, directory);
                if (extension != null)
                {
                    loadedExtensions.Add(extension);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load extension: {ExtensionId}", manifest.Metadata.Id);
            }
        }

        _logger.LogInformation("Successfully loaded {Count} API extension(s)", loadedExtensions.Count);
        return loadedExtensions;
    }

    /// <summary>
    /// Scans a directory for extension manifest files.
    /// </summary>
    private async Task<List<(ExtensionManifest Manifest, string Directory)>> ScanDirectoryForManifestsAsync(string directoryPath)
    {
        var results = new List<(ExtensionManifest, string)>();

        if (!Directory.Exists(directoryPath))
        {
            return results;
        }

        var extensionDirs = Directory.GetDirectories(directoryPath);

        foreach (var extensionDir in extensionDirs)
        {
            var manifestPath = Path.Combine(extensionDir, "extension.manifest.json");

            if (File.Exists(manifestPath))
            {
                try
                {
                    _logger.LogDebug("Found manifest: {Path}", manifestPath);
                    var manifest = ExtensionManifest.LoadFromFile(manifestPath);
                    results.Add((manifest, extensionDir));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load manifest from {Path}", manifestPath);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Resolves extension dependencies and returns them in load order.
    /// Uses topological sort to ensure dependencies are loaded before dependents.
    /// </summary>
    private async Task<List<(ExtensionManifest Manifest, string Directory)>> ResolveDependenciesAsync(
        List<(ExtensionManifest Manifest, string Directory)> manifests)
    {
        // Build dependency graph
        var graph = new Dictionary<string, List<string>>();
        var manifestMap = new Dictionary<string, (ExtensionManifest, string)>();

        foreach (var (manifest, directory) in manifests)
        {
            graph[manifest.Metadata.Id] = manifest.Dependencies.Keys.ToList();
            manifestMap[manifest.Metadata.Id] = (manifest, directory);
        }

        // Topological sort using Kahn's algorithm
        var inDegree = graph.Keys.ToDictionary(k => k, k => 0);

        foreach (var dependencies in graph.Values)
        {
            foreach (var dep in dependencies)
            {
                if (inDegree.ContainsKey(dep))
                {
                    inDegree[dep]++;
                }
                else
                {
                    _logger.LogWarning("Dependency {Dependency} not found", dep);
                }
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<string>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            sorted.Add(node);

            foreach (var dep in graph[node])
            {
                if (inDegree.ContainsKey(dep))
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0)
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        // Check for circular dependencies
        if (sorted.Count != graph.Count)
        {
            var missing = graph.Keys.Except(sorted).ToList();
            _logger.LogError("Circular dependency detected in extensions: {Extensions}", string.Join(", ", missing));
            throw new InvalidOperationException($"Circular dependency detected in extensions: {string.Join(", ", missing)}");
        }

        _logger.LogInformation("Extension load order: {Order}", string.Join(" → ", sorted));

        return sorted.Select(id => manifestMap[id]).ToList();
    }

    /// <summary>
    /// Loads a single extension from its directory.
    /// </summary>
    private async Task<IExtension?> LoadExtensionAsync(ExtensionManifest manifest, string extensionDirectory)
    {
        var extensionId = manifest.Metadata.Id;
        _logger.LogInformation("Loading extension: {ExtensionId} v{Version}", extensionId, manifest.Metadata.Version);

        try
        {
            // Find the API assembly
            var apiAssemblyPath = FindApiAssembly(extensionDirectory, extensionId);
            if (apiAssemblyPath == null)
            {
                _logger.LogWarning("API assembly not found for extension: {ExtensionId}", extensionId);
                return null;
            }

            _logger.LogDebug("Loading assembly: {Path}", apiAssemblyPath);

            // Create isolated load context
            var loadContext = new AssemblyLoadContext($"Extension_{extensionId}", isCollectible: true);

            // Load the assembly
            var assembly = loadContext.LoadFromAssemblyPath(apiAssemblyPath);

            // Find IExtension implementation
            var extensionType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (extensionType == null)
            {
                _logger.LogError("No IExtension implementation found in {Assembly}", apiAssemblyPath);
                return null;
            }

            _logger.LogDebug("Found extension type: {Type}", extensionType.FullName);

            // Create extension instance
            var extension = (IExtension?)Activator.CreateInstance(extensionType);
            if (extension == null)
            {
                _logger.LogError("Failed to create instance of {Type}", extensionType.FullName);
                return null;
            }

            // Store loaded extension info
            _loadedExtensions[extensionId] = new LoadedExtension
            {
                Extension = extension,
                Manifest = manifest,
                LoadContext = loadContext,
                Directory = extensionDirectory
            };

            _logger.LogInformation("Extension loaded successfully: {ExtensionId}", extensionId);
            return extension;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load extension: {ExtensionId}", extensionId);
            return null;
        }
    }

    /// <summary>
    /// Finds the API assembly for an extension.
    /// Searches in bin/Release/net8.0 and bin/Debug/net8.0 directories.
    /// </summary>
    private string? FindApiAssembly(string extensionDirectory, string extensionId)
    {
        var possiblePaths = new[]
        {
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Api", "bin", "Release", "net8.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Api", "bin", "Debug", "net8.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "bin", "Release", "net8.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "bin", "Debug", "net8.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, $"{extensionId}.Api.dll")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found API assembly: {Path}", path);
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all loaded extensions.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedExtension> GetLoadedExtensions() => _loadedExtensions;

    /// <summary>
    /// Gets a loaded extension by ID.
    /// </summary>
    public LoadedExtension? GetExtension(string extensionId)
    {
        return _loadedExtensions.TryGetValue(extensionId, out var extension) ? extension : null;
    }

    /// <summary>
    /// Unloads an extension.
    /// </summary>
    public async Task UnloadExtensionAsync(string extensionId)
    {
        if (!_loadedExtensions.TryGetValue(extensionId, out var loadedExt))
        {
            _logger.LogWarning("Extension not loaded: {ExtensionId}", extensionId);
            return;
        }

        _logger.LogInformation("Unloading extension: {ExtensionId}", extensionId);

        try
        {
            // Dispose extension
            loadedExt.Extension.Dispose();

            // Unload assembly context
            loadedExt.LoadContext?.Unload();

            // Remove from loaded extensions
            _loadedExtensions.Remove(extensionId);

            _logger.LogInformation("Extension unloaded successfully: {ExtensionId}", extensionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading extension: {ExtensionId}", extensionId);
        }
    }
}

/// <summary>
/// Represents a loaded extension with its metadata and load context.
/// </summary>
public class LoadedExtension
{
    public required IExtension Extension { get; set; }
    public required ExtensionManifest Manifest { get; set; }
    public AssemblyLoadContext? LoadContext { get; set; }
    public required string Directory { get; set; }
}
