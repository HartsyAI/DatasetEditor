// TODO: Phase 3 - API Extension Registry
//
// Called by: Program.cs during API server startup
// Calls: ApiExtensionLoader, IExtension.InitializeAsync(), IExtension.ConfigureServices()
//
// Purpose: Discover, load, and manage API-side extensions
// This is the central registry that coordinates all extension loading on the API server.
//
// Responsibilities:
// 1. Scan extension directories for *.Api.dll files
// 2. Load and validate extension manifests
// 3. Resolve extension dependencies
// 4. Load extensions in correct order (respecting dependencies)
// 5. Call ConfigureServices() for each extension
// 6. Call InitializeAsync() for each extension
// 7. Register API endpoints for each extension
// 8. Provide extension lookup and management
//
// Deployment Considerations:
// - This ONLY runs on the API server
// - Extensions with DeploymentTarget.Api or DeploymentTarget.Both are loaded
// - Extensions with DeploymentTarget.Client are ignored
//
// Loading Process:
// 1. Scan Extensions/BuiltIn/ directory
// 2. Find extension.manifest.json files
// 3. Parse manifests and filter by deployment target
// 4. Build dependency graph
// 5. Topological sort for load order
// 6. Load each extension assembly
// 7. Instantiate extension class
// 8. Call lifecycle methods in order

using System.Collections.Concurrent;
using System.Reflection;
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// Registry for discovering and managing API-side extensions.
/// Handles extension lifecycle from discovery through initialization.
/// </summary>
public class ApiExtensionRegistry
{
    private readonly IConfiguration _configuration;
    private readonly IServiceCollection _services;
    private readonly ILogger<ApiExtensionRegistry> _logger;
    private readonly ApiExtensionLoader _loader;
    private readonly ConcurrentDictionary<string, IExtension> _loadedExtensions;
    private readonly ConcurrentDictionary<string, ExtensionManifest> _manifests;
    private bool _initialized;

    /// <summary>
    /// Initializes a new extension registry.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="services">Service collection for DI registration</param>
    public ApiExtensionRegistry(IConfiguration configuration, IServiceCollection services)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // Create logger factory for early logging
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ApiExtensionRegistry>();

        _loader = new ApiExtensionLoader(_logger);
        _loadedExtensions = new ConcurrentDictionary<string, IExtension>();
        _manifests = new ConcurrentDictionary<string, ExtensionManifest>();
    }

    /// <summary>
    /// Discovers and loads all API-side extensions.
    /// Called during application startup, before building the app.
    /// </summary>
    public async Task DiscoverAndLoadAsync()
    {
        if (_initialized)
        {
            _logger.LogWarning("Extension registry already initialized");
            return;
        }

        var enabled = _configuration.GetValue<bool>("Extensions:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Extensions are disabled in configuration");
            return;
        }

        _logger.LogInformation("Discovering API extensions...");

        // Get extension directories from configuration
        var builtInDir = _configuration.GetValue<string>("Extensions:Directory") ?? "./Extensions/BuiltIn";
        var userDir = _configuration.GetValue<string>("Extensions:UserDirectory") ?? "./Extensions/User";

        // Discover extensions in both directories
        var builtInManifests = await DiscoverExtensionsInDirectoryAsync(builtInDir);
        var userManifests = await DiscoverExtensionsInDirectoryAsync(userDir);

        var allManifests = builtInManifests.Concat(userManifests).ToList();

        // Filter to API-side extensions only
        var apiManifests = allManifests
            .Where(m => m.DeploymentTarget == ExtensionDeploymentTarget.Api ||
                       m.DeploymentTarget == ExtensionDeploymentTarget.Both)
            .ToList();

        _logger.LogInformation("Found {Count} API extensions to load", apiManifests.Count);

        // Resolve dependencies and determine load order
        var loadOrder = ResolveDependencies(apiManifests);

        // Load extensions in dependency order
        foreach (var manifest in loadOrder)
        {
            try
            {
                _logger.LogInformation("Loading extension: {ExtensionId}", manifest.Metadata.Id);

                // Load the extension
                var extension = await _loader.LoadExtensionAsync(manifest);

                // Call ConfigureServices
                extension.ConfigureServices(_services);

                // Store for later initialization
                _loadedExtensions[manifest.Metadata.Id] = extension;
                _manifests[manifest.Metadata.Id] = manifest;

                _logger.LogInformation("Extension loaded: {ExtensionId}", manifest.Metadata.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load extension: {ExtensionId}", manifest.Metadata.Id);
                // TODO: Phase 3 - Add option to continue on error or fail fast
            }
        }

        _initialized = true;
    }

    /// <summary>
    /// Configures loaded extensions after the application is built.
    /// Called after builder.Build() in Program.cs.
    /// </summary>
    public async Task ConfigureExtensionsAsync(IApplicationBuilder app)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Extensions not loaded - skipping configuration");
            return;
        }

        _logger.LogInformation("Configuring {Count} API extensions...", _loadedExtensions.Count);

        var serviceProvider = app.ApplicationServices;

        foreach (var (extensionId, extension) in _loadedExtensions)
        {
            try
            {
                _logger.LogInformation("Configuring extension: {ExtensionId}", extensionId);

                // Configure app pipeline (register endpoints, middleware, etc.)
                extension.ConfigureApp(app);

                // Initialize extension with context
                var manifest = _manifests[extensionId];
                var context = CreateExtensionContext(manifest, serviceProvider);
                await extension.InitializeAsync(context);

                // Validate extension
                var isValid = await extension.ValidateAsync();
                if (!isValid)
                {
                    _logger.LogWarning("Extension validation failed: {ExtensionId}", extensionId);
                }

                _logger.LogInformation("Extension configured successfully: {ExtensionId}", extensionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure extension: {ExtensionId}", extensionId);
            }
        }
    }

    /// <summary>
    /// Gets a loaded extension by ID.
    /// </summary>
    public IExtension? GetExtension(string extensionId)
    {
        _loadedExtensions.TryGetValue(extensionId, out var extension);
        return extension;
    }

    /// <summary>
    /// Gets all loaded extensions.
    /// </summary>
    public IReadOnlyDictionary<string, IExtension> GetAllExtensions()
    {
        return _loadedExtensions;
    }

    /// <summary>
    /// Discovers extensions in a directory by scanning for manifest files.
    /// </summary>
    private async Task<List<ExtensionManifest>> DiscoverExtensionsInDirectoryAsync(string directory)
    {
        var manifests = new List<ExtensionManifest>();

        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Extension directory not found: {Directory}", directory);
            return manifests;
        }

        // Find all extension.manifest.json files recursively
        var manifestFiles = Directory.GetFiles(
            directory,
            ExtensionManifest.ManifestFileName,
            SearchOption.AllDirectories);

        _logger.LogDebug("Found {Count} manifest files in {Directory}", manifestFiles.Length, directory);

        foreach (var manifestFile in manifestFiles)
        {
            try
            {
                _logger.LogDebug("Loading manifest: {ManifestFile}", manifestFile);

                var manifest = ExtensionManifest.LoadFromFile(manifestFile);
                manifests.Add(manifest);

                _logger.LogDebug("Loaded manifest for extension: {ExtensionId}", manifest.Metadata.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load manifest: {ManifestFile}", manifestFile);
            }
        }

        return manifests;
    }

    /// <summary>
    /// Resolves extension dependencies and returns extensions in load order.
    /// Uses topological sort to ensure dependencies are loaded first.
    /// </summary>
    private List<ExtensionManifest> ResolveDependencies(List<ExtensionManifest> manifests)
    {
        // TODO: Phase 3 - Implement dependency resolution with topological sort
        // For now, return in original order
        _logger.LogDebug("Resolving dependencies for {Count} extensions", manifests.Count);

        // Build dependency graph
        // Detect circular dependencies
        // Topological sort
        // Return ordered list

        return manifests;
    }

    /// <summary>
    /// Creates an extension context for initialization.
    /// </summary>
    private IExtensionContext CreateExtensionContext(
        ExtensionManifest manifest,
        IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Extension.{manifest.Metadata.Id}");

        var extensionConfig = _configuration.GetSection($"Extensions:{manifest.Metadata.Id}");

        return new ExtensionContextBuilder()
            .WithManifest(manifest)
            .WithServices(serviceProvider)
            .WithConfiguration(extensionConfig)
            .WithLogger(logger)
            .WithEnvironment(ExtensionEnvironment.Api)
            .WithExtensionDirectory(manifest.DirectoryPath ?? "./Extensions/BuiltIn")
            .Build();
    }
}
