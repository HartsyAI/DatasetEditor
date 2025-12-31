using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace DatasetStudio.ClientApp.Services.Extensions;

/// <summary>
/// Manages discovery, loading, and lifecycle of Client-side extensions.
/// Scans Extensions/BuiltIn and Extensions/Community directories for extensions.
/// </summary>
public class ClientExtensionRegistry
{
    private readonly ILogger<ClientExtensionRegistry> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, LoadedClientExtension> _loadedExtensions = new();
    private readonly string _builtInExtensionsPath;
    private readonly string _communityExtensionsPath;

    public ClientExtensionRegistry(
        ILogger<ClientExtensionRegistry> logger,
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
        _logger.LogInformation("Discovering Client extensions...");

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
            .Where(m => m.Manifest.DeploymentTarget == ExtensionDeploymentTarget.Client ||
                       m.Manifest.DeploymentTarget == ExtensionDeploymentTarget.Both)
            .ToList();

        _logger.LogInformation("Total Client extensions to load: {Count}", manifests.Count);

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

        _logger.LogInformation("Successfully loaded {Count} Client extension(s)", loadedExtensions.Count);
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
            // Find the Client assembly
            var clientAssemblyPath = FindClientAssembly(extensionDirectory, extensionId);
            if (clientAssemblyPath == null)
            {
                _logger.LogWarning("Client assembly not found for extension: {ExtensionId}", extensionId);
                return null;
            }

            _logger.LogDebug("Loading assembly: {Path}", clientAssemblyPath);

            // Load the assembly
            var assembly = Assembly.LoadFrom(clientAssemblyPath);

            // Find IExtension implementation
            var extensionType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (extensionType == null)
            {
                _logger.LogError("No IExtension implementation found in {Assembly}", clientAssemblyPath);
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
            _loadedExtensions[extensionId] = new LoadedClientExtension
            {
                Extension = extension,
                Manifest = manifest,
                Directory = extensionDirectory,
                Assembly = assembly
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
    /// Finds the Client assembly for an extension.
    /// Searches in bin/Release/net8.0 and bin/Debug/net8.0 directories.
    /// </summary>
    private string? FindClientAssembly(string extensionDirectory, string extensionId)
    {
        var possiblePaths = new[]
        {
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Client", "bin", "Release", "net8.0", $"{extensionId}.Client.dll"),
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Client", "bin", "Debug", "net8.0", $"{extensionId}.Client.dll"),
            Path.Combine(extensionDirectory, "bin", "Release", "net8.0", $"{extensionId}.Client.dll"),
            Path.Combine(extensionDirectory, "bin", "Debug", "net8.0", $"{extensionId}.Client.dll"),
            Path.Combine(extensionDirectory, $"{extensionId}.Client.dll")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found Client assembly: {Path}", path);
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all loaded extensions.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedClientExtension> GetLoadedExtensions() => _loadedExtensions;

    /// <summary>
    /// Gets a loaded extension by ID.
    /// </summary>
    public LoadedClientExtension? GetExtension(string extensionId)
    {
        return _loadedExtensions.TryGetValue(extensionId, out var extension) ? extension : null;
    }
}

/// <summary>
/// Represents a loaded client extension with its metadata.
/// </summary>
public class LoadedClientExtension
{
    public required IExtension Extension { get; set; }
    public required ExtensionManifest Manifest { get; set; }
    public required string Directory { get; set; }
    public Assembly? Assembly { get; set; }
}
