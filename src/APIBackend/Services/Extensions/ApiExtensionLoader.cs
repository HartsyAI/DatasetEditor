using System.Reflection;
using System.Runtime.Loader;
using DatasetStudio.Extensions.SDK;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// Finds API extensions in the built-in and community folders, loads their assemblies, lets them
/// register services before the host is built and maps their endpoints after.
/// </summary>
/// <remarks>
/// Everything here runs once at startup, synchronously: it is local file I/O and assembly loading,
/// and the old async methods never awaited anything (audit X-4). Extensions are never unloaded;
/// the unload method had no callers and was removed (audit X-2).
/// </remarks>
public sealed class ApiExtensionLoader
{
    private readonly ILogger<ApiExtensionLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _builtInExtensionsPath;
    private readonly string _communityExtensionsPath;

    /// <summary>
    /// Creates a loader. It needs no service provider, so Program.cs can run it before the host is
    /// built without building a throwaway provider (audit C-5).
    /// </summary>
    /// <param name="logger">Startup logger.</param>
    /// <param name="configuration">The API's configuration (reads the <c>Extensions</c> section).</param>
    /// <param name="contentRootPath">The API's content root, used to find the default folders.</param>
    public ApiExtensionLoader(ILogger<ApiExtensionLoader> logger, IConfiguration configuration, string contentRootPath)
    {
        _logger = logger;
        _configuration = configuration;

        // Extensions live at repo-root `src/Extensions/{BuiltIn,Community}`, one level up
        // from this project's own content root (`src/APIBackend`) — NOT under
        // Directory.GetCurrentDirectory(), which under `dotnet run --project src/APIBackend`
        // *is* src/APIBackend, so "Extensions/BuiltIn" relative to it never resolved to
        // anything and every built-in extension silently failed to load. Configurable via
        // Extensions:BuiltInPath/Extensions:CommunityPath (the Docker image uses the defaults,
        // which resolve to /Extensions/{BuiltIn,Community} next to /app).
        string defaultBuiltIn = Path.GetFullPath(Path.Combine(contentRootPath, "..", "Extensions", "BuiltIn"));
        string defaultCommunity = Path.GetFullPath(Path.Combine(contentRootPath, "..", "Extensions", "Community"));
        _builtInExtensionsPath = configuration["Extensions:BuiltInPath"] is { Length: > 0 } builtInOverride
            ? Path.GetFullPath(builtInOverride)
            : defaultBuiltIn;
        _communityExtensionsPath = configuration["Extensions:CommunityPath"] is { Length: > 0 } communityOverride
            ? Path.GetFullPath(communityOverride)
            : defaultCommunity;
    }

    /// <summary>
    /// Discovers every enabled API extension, loads its assembly and creates its instance.
    /// An extension that fails to load is logged and skipped; it never stops the API starting.
    /// </summary>
    /// <returns>The loaded extensions, in load order.</returns>
    public IReadOnlyList<LoadedApiExtension> LoadAll()
    {
        _logger.LogInformation("Discovering API extensions...");

        List<(ExtensionManifest Manifest, string Directory)> manifests = new();
        manifests.AddRange(ScanDirectoryForManifests(_builtInExtensionsPath, "built-in"));
        manifests.AddRange(ScanDirectoryForManifests(_communityExtensionsPath, "community"));

        manifests = manifests
            .Where(m => m.Manifest.DeploymentTarget is ExtensionDeploymentTarget.Api or ExtensionDeploymentTarget.Both)
            .ToList();

        List<string> disabledExtensions = _configuration.GetSection("Extensions:DisabledExtensions").Get<List<string>>() ?? new List<string>();
        if (disabledExtensions.Count > 0)
        {
            _logger.LogInformation("Disabled extensions: {Extensions}", string.Join(", ", disabledExtensions));
            manifests = manifests
                .Where(m => !disabledExtensions.Contains(m.Manifest.Metadata.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        _logger.LogInformation("Total API extensions to load: {Count}", manifests.Count);

        List<LoadedApiExtension> loaded = new();
        foreach ((ExtensionManifest manifest, string directory) in ResolveDependencies(manifests))
        {
            LoadedApiExtension? extension = LoadExtension(manifest, directory);
            if (extension is not null)
            {
                loaded.Add(extension);
            }
        }

        _logger.LogInformation("Successfully loaded {Count} API extension(s)", loaded.Count);
        return loaded;
    }

    /// <summary>
    /// Calls each extension's <see cref="IApiExtension.ConfigureServices"/>. An extension that
    /// throws is logged and dropped, so its endpoints are never mapped against services it failed
    /// to register.
    /// </summary>
    /// <param name="extensions">Extensions returned by <see cref="LoadAll"/>.</param>
    /// <param name="services">The API's service collection.</param>
    /// <returns>The extensions whose services were registered.</returns>
    public IReadOnlyList<LoadedApiExtension> ConfigureServices(IReadOnlyList<LoadedApiExtension> extensions, IServiceCollection services)
    {
        List<LoadedApiExtension> configured = new();
        foreach (LoadedApiExtension extension in extensions)
        {
            try
            {
                extension.Instance.ConfigureServices(services, _configuration);
                configured.Add(extension);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure services for extension: {ExtensionId}", extension.Manifest.Metadata.Id);
            }
        }

        return configured;
    }

    /// <summary>
    /// Maps each extension's endpoints into its own route group at <c>/api/extensions/{id}</c>
    /// that requires an authenticated user, so an extension cannot add an anonymous route by
    /// omission (the CoreViewer placeholder routes were anonymous, audit X-5).
    /// </summary>
    /// <param name="endpoints">The built application.</param>
    /// <param name="extensions">Extensions returned by <see cref="ConfigureServices"/>.</param>
    /// <param name="logger">Application logger.</param>
    public static void MapEndpoints(IEndpointRouteBuilder endpoints, IReadOnlyList<LoadedApiExtension> extensions, ILogger logger)
    {
        foreach (LoadedApiExtension extension in extensions)
        {
            string extensionId = extension.Manifest.Metadata.Id;
            try
            {
                RouteGroupBuilder group = endpoints
                    .MapGroup($"/api/extensions/{extensionId.ToLowerInvariant()}")
                    .RequireAuthorization();
                extension.Instance.MapEndpoints(group);
                logger.LogInformation("Extension ready: {ExtensionId} v{Version}", extensionId, extension.Manifest.Metadata.Version);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to map endpoints for extension: {ExtensionId}", extensionId);
            }
        }
    }

    /// <summary>
    /// Reads the manifest in each immediate subfolder of <paramref name="directoryPath"/>.
    /// </summary>
    private List<(ExtensionManifest Manifest, string Directory)> ScanDirectoryForManifests(string directoryPath, string kind)
    {
        List<(ExtensionManifest Manifest, string Directory)> results = new();
        if (!Directory.Exists(directoryPath))
        {
            return results;
        }

        foreach (string extensionDir in Directory.GetDirectories(directoryPath))
        {
            string manifestPath = Path.Combine(extensionDir, ExtensionManifest.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                results.Add((ExtensionManifest.LoadFromFile(manifestPath), extensionDir));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load manifest from {Path}", manifestPath);
            }
        }

        _logger.LogInformation("Found {Count} {Kind} extension(s) in {Path}", results.Count, kind, directoryPath);
        return results;
    }

    /// <summary>
    /// Resolves extension dependencies and returns them in load order.
    /// Uses topological sort to ensure dependencies are loaded before dependents.
    /// </summary>
    private List<(ExtensionManifest Manifest, string Directory)> ResolveDependencies(
        List<(ExtensionManifest Manifest, string Directory)> manifests)
    {
        // Build dependency graph
        Dictionary<string, List<string>> graph = new();
        Dictionary<string, (ExtensionManifest, string)> manifestMap = new();

        foreach ((ExtensionManifest manifest, string directory) in manifests)
        {
            graph[manifest.Metadata.Id] = manifest.Dependencies.Keys.ToList();
            manifestMap[manifest.Metadata.Id] = (manifest, directory);
        }

        // Topological sort using Kahn's algorithm
        Dictionary<string, int> inDegree = graph.Keys.ToDictionary(k => k, k => 0);

        foreach (List<string> dependencies in graph.Values)
        {
            foreach (string dep in dependencies)
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

        Queue<string> queue = new(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        List<string> sorted = new();

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            sorted.Add(node);

            foreach (string dep in graph[node])
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
            List<string> missing = graph.Keys.Except(sorted).ToList();
            _logger.LogError("Circular dependency detected in extensions: {Extensions}", string.Join(", ", missing));
            throw new InvalidOperationException($"Circular dependency detected in extensions: {string.Join(", ", missing)}");
        }

        _logger.LogInformation("Extension load order: {Order}", string.Join(" → ", sorted));

        return sorted.Select(id => manifestMap[id]).ToList();
    }

    /// <summary>
    /// Loads one extension's API assembly and creates its <see cref="IApiExtension"/>.
    /// </summary>
    private LoadedApiExtension? LoadExtension(ExtensionManifest manifest, string extensionDirectory)
    {
        string extensionId = manifest.Metadata.Id;
        _logger.LogInformation("Loading extension: {ExtensionId} v{Version}", extensionId, manifest.Metadata.Version);

        try
        {
            string? apiAssemblyPath = FindApiAssembly(extensionDirectory, extensionId);
            if (apiAssemblyPath is null)
            {
                _logger.LogWarning("API assembly not found for extension: {ExtensionId}", extensionId);
                return null;
            }

            _logger.LogDebug("Loading assembly: {Path}", apiAssemblyPath);
            AssemblyLoadContext loadContext = new($"Extension_{extensionId}");
            Assembly assembly = loadContext.LoadFromAssemblyPath(apiAssemblyPath);

            Type? extensionType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IApiExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
            if (extensionType is null)
            {
                _logger.LogError("No IApiExtension implementation found in {Assembly}", apiAssemblyPath);
                return null;
            }

            IApiExtension instance = (IApiExtension)Activator.CreateInstance(extensionType)!;
            _logger.LogInformation("Extension loaded: {ExtensionId} from {Path}", extensionId, apiAssemblyPath);
            return new LoadedApiExtension(manifest, extensionDirectory, instance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load extension: {ExtensionId}", extensionId);
            return null;
        }
    }

    /// <summary>
    /// Finds the API assembly for an extension.
    /// Searches in bin/Release/net10.0 and bin/Debug/net10.0 directories (the solution-wide
    /// target, Directory.Build.props). Update this list when the target framework changes.
    /// </summary>
    private string? FindApiAssembly(string extensionDirectory, string extensionId)
    {
        string[] possiblePaths =
        [
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Api", "bin", "Release", "net10.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "src", $"{extensionId}.Api", "bin", "Debug", "net10.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "bin", "Release", "net10.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, "bin", "Debug", "net10.0", $"{extensionId}.Api.dll"),
            Path.Combine(extensionDirectory, $"{extensionId}.Api.dll")
        ];

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found API assembly: {Path}", path);
                return path;
            }
        }

        return null;
    }
}
