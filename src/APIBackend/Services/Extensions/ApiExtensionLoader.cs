using System.Reflection;
using System.Runtime.Versioning;
using DatasetStudio.Extensions.SDK;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// Finds API extensions in the built-in and community folders, loads their assemblies, lets them
/// register services before the host is built and maps their endpoints after.
/// </summary>
/// <remarks>
/// Everything here runs once at startup, synchronously: it is local file I/O and assembly loading,
/// and the old async methods never awaited anything (audit X-4). Each extension gets its own
/// <see cref="ExtensionLoadContext"/>. Extensions are never unloaded; the unload method had no
/// callers and was removed (audit X-2).
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

        // ExtensionManifest has reference equality, so two folders with the same id stay distinct keys.
        Dictionary<ExtensionManifest, string> directories = manifests.ToDictionary(m => m.Manifest, m => m.Directory);
        IReadOnlyList<ExtensionManifest> ordered = OrderByDependencies(manifests.Select(m => m.Manifest).ToList(), _logger);
        _logger.LogInformation("Extension load order: {Order}", string.Join(" -> ", ordered.Select(m => m.Metadata.Id)));

        List<LoadedApiExtension> loaded = new();
        HashSet<string> loadedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExtensionManifest manifest in ordered)
        {
            // Dependencies come first, so one that failed to load is already known here.
            string? failedDependency = manifest.Dependencies.Keys.FirstOrDefault(dependency => !loadedIds.Contains(dependency));
            if (failedDependency is not null)
            {
                _logger.LogError("Skipping extension {ExtensionId}: its dependency {Dependency} failed to load", manifest.Metadata.Id, failedDependency);
                continue;
            }

            LoadedApiExtension? extension = LoadExtension(manifest, directories[manifest]);
            if (extension is not null)
            {
                loaded.Add(extension);
                loadedIds.Add(manifest.Metadata.Id);
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
    /// Orders manifests so every extension comes after the extensions it depends on, keeping
    /// discovery order otherwise. Drops a duplicate id (the first one found wins), an extension
    /// whose dependency is not in the list, and every extension in or behind a dependency cycle;
    /// each drop is logged, none stops startup.
    /// </summary>
    /// <remarks>
    /// Audit X-4: the old Kahn's algorithm incremented the in-degree of each dependency instead of
    /// each dependent, so it emitted dependents first. It also only warned about a missing
    /// dependency and then loaded the dependent anyway, and threw on a cycle, which stopped the
    /// API from starting.
    /// </remarks>
    /// <param name="manifests">Manifests in discovery order (built-in first, then community).</param>
    /// <param name="logger">Where drops are reported.</param>
    /// <returns>The manifests to load, in load order.</returns>
    internal static IReadOnlyList<ExtensionManifest> OrderByDependencies(IReadOnlyList<ExtensionManifest> manifests, ILogger logger)
    {
        Dictionary<string, ExtensionManifest> byId = new(StringComparer.OrdinalIgnoreCase);
        List<string> discoveryOrder = new();
        foreach (ExtensionManifest manifest in manifests)
        {
            if (byId.TryAdd(manifest.Metadata.Id, manifest))
            {
                discoveryOrder.Add(manifest.Metadata.Id);
            }
            else
            {
                logger.LogError("Extension id {ExtensionId} is used by more than one extension folder; loading only the first one found", manifest.Metadata.Id);
            }
        }

        // Drop extensions whose dependencies are not installed or are disabled, repeating until
        // nothing changes so a dependent of a dropped extension is dropped too.
        bool dropped;
        do
        {
            dropped = false;
            foreach (string id in discoveryOrder.Where(byId.ContainsKey).ToList())
            {
                string? missing = byId[id].Dependencies.Keys.FirstOrDefault(dependency => !byId.ContainsKey(dependency));
                if (missing is not null)
                {
                    logger.LogError("Skipping extension {ExtensionId}: it depends on {Dependency}, which is not installed, is disabled or was skipped", id, missing);
                    byId.Remove(id);
                    dropped = true;
                }
            }
        }
        while (dropped);

        // Kahn's algorithm with edges dependency -> dependent: an extension is ready once all of
        // its dependencies have been emitted.
        Dictionary<string, int> unmetDependencies = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> dependents = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, ExtensionManifest manifest) in byId)
        {
            List<string> dependencies = manifest.Dependencies.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            unmetDependencies[id] = dependencies.Count;
            foreach (string dependency in dependencies)
            {
                if (!dependents.TryGetValue(dependency, out List<string>? list))
                {
                    list = new List<string>();
                    dependents[dependency] = list;
                }
                list.Add(id);
            }
        }

        Queue<string> ready = new(discoveryOrder.Where(id => byId.ContainsKey(id) && unmetDependencies[id] == 0));
        List<ExtensionManifest> ordered = new();
        while (ready.Count > 0)
        {
            string id = ready.Dequeue();
            ordered.Add(byId[id]);
            if (!dependents.TryGetValue(id, out List<string>? waiting))
            {
                continue;
            }

            // Dependents become ready in discovery order, so the result is deterministic.
            foreach (string dependent in discoveryOrder.Where(waiting.Contains))
            {
                unmetDependencies[dependent]--;
                if (unmetDependencies[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (ordered.Count < byId.Count)
        {
            IEnumerable<string> stuck = discoveryOrder.Where(id => byId.ContainsKey(id) && !ordered.Contains(byId[id]));
            logger.LogError("Skipping extensions with a circular dependency (or depending on one): {Extensions}", string.Join(", ", stuck));
        }

        return ordered;
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
                _logger.LogWarning(
                    "API assembly not found for extension {ExtensionId}: expected {Published} or {Built}",
                    extensionId,
                    Path.Combine(extensionDirectory, $"{extensionId}.Api.dll"),
                    Path.Combine(extensionDirectory, $"{extensionId}.Api", "bin", "{Configuration}", HostTargetFramework, $"{extensionId}.Api.dll"));
                return null;
            }

            _logger.LogDebug("Loading assembly: {Path}", apiAssemblyPath);
            ExtensionLoadContext loadContext = new(extensionId, apiAssemblyPath);
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
    /// The API's own target framework moniker, e.g. <c>net10.0</c>, which is the folder an
    /// extension built for this API lands in under <c>bin/{Configuration}/</c>. Read from the
    /// API assembly rather than hard-coded: the old probe list hard-coded the TFM and went stale
    /// when the solution moved frameworks (audit X-1).
    /// </summary>
    internal static string HostTargetFramework { get; } = ReadHostTargetFramework();

    private static string ReadHostTargetFramework()
    {
        string? frameworkName = typeof(ApiExtensionLoader).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        Version version = frameworkName is not null ? new FrameworkName(frameworkName).Version : Environment.Version;
        return $"net{version.Major}.{version.Minor}";
    }

    /// <summary>
    /// Finds an extension's <c>{Id}.Api.dll</c> in one of the two layouts that actually occur.
    /// </summary>
    /// <remarks>
    /// Audit X-1: the old probe list looked under <c>{extension}/src/{Id}.Api/bin/...</c> and
    /// <c>{extension}/bin/...</c>, neither of which is where <c>dotnet build</c> writes the
    /// assembly, so under <c>dotnet run</c> every extension logged "API assembly not found".
    /// </remarks>
    private string? FindApiAssembly(string extensionDirectory, string extensionId)
    {
        string assemblyFileName = $"{extensionId}.Api.dll";

        // Published layout (the Dockerfile): `dotnet publish -o {extension}` puts the assembly
        // flat next to the manifest.
        string publishedPath = Path.Combine(extensionDirectory, assemblyFileName);
        if (File.Exists(publishedPath))
        {
            return publishedPath;
        }

        // Build layout (`dotnet build` / `dotnet run`): {extension}/{Id}.Api/bin/{Configuration}/{tfm}/.
        // Any configuration name counts; when several exist, the newest build wins so a stale
        // Release build never shadows the Debug build you just made.
        string projectBin = Path.Combine(extensionDirectory, $"{extensionId}.Api", "bin");
        if (!Directory.Exists(projectBin))
        {
            return null;
        }

        return Directory.EnumerateDirectories(projectBin)
            .Select(configurationDirectory => Path.Combine(configurationDirectory, HostTargetFramework, assemblyFileName))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
