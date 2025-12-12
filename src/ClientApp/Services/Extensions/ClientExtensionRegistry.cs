// TODO: Phase 3 - Client Extension Registry
//
// Called by: Program.cs during Blazor WebAssembly startup
// Calls: ClientExtensionLoader, IExtension.InitializeAsync(), IExtension.ConfigureServices()
//
// Purpose: Discover, load, and manage Client-side extensions (Blazor components)
// This is the central registry for all extension loading in the Blazor WASM app.
//
// Responsibilities:
// 1. Scan extension directories for *.Client.dll files
// 2. Load and validate extension manifests
// 3. Resolve extension dependencies
// 4. Load extensions in correct order
// 5. Call ConfigureServices() for each extension
// 6. Register Blazor components dynamically
// 7. Register navigation menu items
// 8. Call InitializeAsync() for each extension
// 9. Configure HttpClient for API communication
//
// CRITICAL for Distributed Deployments:
// - This runs in the browser (Blazor WebAssembly)
// - Extensions with DeploymentTarget.Client or DeploymentTarget.Both are loaded
// - Extensions with DeploymentTarget.Api are ignored
// - HttpClient is configured with API base URL for remote API calls
//
// Loading Process (similar to API but for Client):
// 1. Scan Extensions/BuiltIn/ directory (deployed with WASM app)
// 2. Find extension.manifest.json files
// 3. Parse manifests and filter by deployment target
// 4. Build dependency graph
// 5. Load each extension assembly
// 6. Register Blazor components and routes
// 7. Call lifecycle methods

using System.Collections.Concurrent;
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.ClientApp.Services.Extensions;

/// <summary>
/// Registry for discovering and managing Client-side extensions in Blazor WebAssembly.
/// Handles extension lifecycle from discovery through initialization.
/// </summary>
public class ClientExtensionRegistry
{
    private readonly IConfiguration _configuration;
    private readonly IServiceCollection _services;
    private readonly ILogger<ClientExtensionRegistry> _logger;
    private readonly ClientExtensionLoader _loader;
    private readonly ConcurrentDictionary<string, IExtension> _loadedExtensions;
    private readonly ConcurrentDictionary<string, ExtensionManifest> _manifests;
    private bool _initialized;

    /// <summary>
    /// Initializes a new client extension registry.
    /// </summary>
    public ClientExtensionRegistry(IConfiguration configuration, IServiceCollection services)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // Create logger factory for early logging
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ClientExtensionRegistry>();

        _loader = new ClientExtensionLoader(_logger);
        _loadedExtensions = new ConcurrentDictionary<string, IExtension>();
        _manifests = new ConcurrentDictionary<string, ExtensionManifest>();
    }

    /// <summary>
    /// Discovers and loads all Client-side extensions.
    /// Called during Blazor app startup, before building the host.
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

        _logger.LogInformation("Discovering Client extensions...");

        // Get extension directory from configuration
        var extensionDir = _configuration.GetValue<string>("Extensions:Directory") ?? "./Extensions/BuiltIn";

        // Discover extensions
        var manifests = await DiscoverExtensionsInDirectoryAsync(extensionDir);

        // Filter to Client-side extensions only
        var clientManifests = manifests
            .Where(m => m.DeploymentTarget == ExtensionDeploymentTarget.Client ||
                       m.DeploymentTarget == ExtensionDeploymentTarget.Both)
            .ToList();

        _logger.LogInformation("Found {Count} Client extensions to load", clientManifests.Count);

        // Get API base URL for HttpClient configuration
        var apiBaseUrl = _configuration.GetValue<string>("Api:BaseUrl")
            ?? throw new InvalidOperationException("Api:BaseUrl not configured in appsettings.json");

        // Resolve dependencies and determine load order
        var loadOrder = ResolveDependencies(clientManifests);

        // Load extensions in dependency order
        foreach (var manifest in loadOrder)
        {
            try
            {
                _logger.LogInformation("Loading extension: {ExtensionId}", manifest.Metadata.Id);

                // Load the extension
                var extension = await _loader.LoadExtensionAsync(manifest);

                // Configure HttpClient for this extension
                ConfigureExtensionHttpClient(manifest.Metadata.Id, apiBaseUrl);

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
            }
        }

        _initialized = true;
    }

    /// <summary>
    /// Configures loaded extensions after the application is built.
    /// Called after builder.Build() in Program.cs.
    /// </summary>
    public async Task ConfigureExtensionsAsync()
    {
        if (!_initialized)
        {
            _logger.LogWarning("Extensions not loaded - skipping configuration");
            return;
        }

        _logger.LogInformation("Configuring {Count} Client extensions...", _loadedExtensions.Count);

        // Note: In Blazor WASM, we don't have an IApplicationBuilder
        // Configuration happens through service provider

        foreach (var (extensionId, extension) in _loadedExtensions)
        {
            try
            {
                _logger.LogInformation("Configuring extension: {ExtensionId}", extensionId);

                // Create extension context
                var manifest = _manifests[extensionId];
                var context = await CreateExtensionContextAsync(manifest);

                // Initialize extension
                await extension.InitializeAsync(context);

                // Register components if this is a BaseClientExtension
                if (extension is BaseClientExtension clientExtension)
                {
                    clientExtension.RegisterComponents();
                    clientExtension.RegisterNavigation();
                }

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

        // TODO: Phase 3 - In Blazor WASM, we can't use Directory.GetFiles
        // Instead, we need to:
        // 1. Pre-compile list of extensions at build time
        // 2. Or use HTTP to fetch manifest files from wwwroot
        // 3. Or embed manifests as resources

        _logger.LogDebug("Discovering extensions in: {Directory}", directory);

        // For now, return empty list
        // Implementation will be completed when manifest loading is ready

        return manifests;
    }

    /// <summary>
    /// Resolves extension dependencies and returns extensions in load order.
    /// </summary>
    private List<ExtensionManifest> ResolveDependencies(List<ExtensionManifest> manifests)
    {
        // TODO: Phase 3 - Implement dependency resolution
        _logger.LogDebug("Resolving dependencies for {Count} extensions", manifests.Count);
        return manifests;
    }

    /// <summary>
    /// Configures HttpClient for an extension to call its API endpoints.
    /// </summary>
    private void ConfigureExtensionHttpClient(string extensionId, string apiBaseUrl)
    {
        _services.AddHttpClient($"Extension_{extensionId}", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.DefaultRequestHeaders.Add("X-Extension-Id", extensionId);
        });

        _logger.LogDebug(
            "Configured HttpClient for extension {ExtensionId} with API base URL: {ApiBaseUrl}",
            extensionId,
            apiBaseUrl);
    }

    /// <summary>
    /// Creates an extension context for initialization.
    /// </summary>
    private async Task<IExtensionContext> CreateExtensionContextAsync(ExtensionManifest manifest)
    {
        // Build a temporary service provider to get required services
        var serviceProvider = _services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Extension.{manifest.Metadata.Id}");

        var extensionConfig = _configuration.GetSection($"Extensions:{manifest.Metadata.Id}");

        // Get HttpClient for API calls
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient($"Extension_{manifest.Metadata.Id}");

        return new ExtensionContextBuilder()
            .WithManifest(manifest)
            .WithServices(serviceProvider)
            .WithConfiguration(extensionConfig)
            .WithLogger(logger)
            .WithEnvironment(ExtensionEnvironment.Client)
            .WithExtensionDirectory(manifest.DirectoryPath ?? "./Extensions/BuiltIn")
            .WithApiClient(httpClient)
            .Build();
    }
}
