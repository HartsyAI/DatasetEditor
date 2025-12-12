// TODO: Phase 3 - Extension Context
//
// Purpose: Shared state and configuration container for extensions
// Provides access to core services, configuration, logging, and communication
//
// Called by: Extension loader when initializing extensions (via IExtension.InitializeAsync)
// Calls: IServiceProvider, IConfiguration, ILogger, HttpClient
//
// Key Responsibilities:
// 1. Provide access to DI services
// 2. Provide extension-specific configuration
// 3. Provide structured logging
// 4. Provide HTTP client for API communication (Client extensions)
// 5. Provide extension metadata
//
// Deployment Scenarios:
// - API Context: Services include DB, file system, background workers
// - Client Context: Services include HttpClient, local storage, Blazor services
// - Both: Context is created separately on each side with appropriate services
//
// Thread Safety: Context instances are immutable after creation (safe for concurrent access)

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Provides context and services to extensions during initialization and execution.
/// This is the main communication channel between the core system and extensions.
/// </summary>
public interface IExtensionContext
{
    /// <summary>
    /// Gets the extension manifest for this extension.
    /// </summary>
    ExtensionManifest Manifest { get; }

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// Use this to resolve services registered in ConfigureServices().
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Gets the configuration for this extension.
    /// Configuration is loaded from appsettings.json under "Extensions:{ExtensionId}".
    /// </summary>
    IConfiguration Configuration { get; }

    /// <summary>
    /// Gets the logger for this extension.
    /// All log messages are automatically tagged with the extension ID.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Gets the deployment environment (API or Client).
    /// Use this to conditionally execute code based on where the extension is running.
    /// </summary>
    ExtensionEnvironment Environment { get; }

    /// <summary>
    /// Gets the HTTP client for making API calls (Client extensions only).
    /// Pre-configured with the API base URL from appsettings.
    /// Returns null for API-side extensions.
    /// </summary>
    HttpClient? ApiClient { get; }

    /// <summary>
    /// Gets the root directory where this extension is installed.
    /// Useful for loading extension-specific resources, templates, etc.
    /// </summary>
    string ExtensionDirectory { get; }

    /// <summary>
    /// Gets or sets custom extension-specific data.
    /// Use this to share state between different parts of your extension.
    /// Thread-safe for read/write operations.
    /// </summary>
    IDictionary<string, object> Data { get; }
}

/// <summary>
/// Concrete implementation of IExtensionContext.
/// Created by the extension loader during extension initialization.
/// </summary>
public class ExtensionContext : IExtensionContext
{
    /// <summary>
    /// Initializes a new extension context.
    /// </summary>
    /// <param name="manifest">Extension manifest</param>
    /// <param name="services">Service provider for DI</param>
    /// <param name="configuration">Extension configuration</param>
    /// <param name="logger">Logger for this extension</param>
    /// <param name="environment">Deployment environment (API or Client)</param>
    /// <param name="extensionDirectory">Root directory of the extension</param>
    /// <param name="apiClient">HTTP client for API calls (Client only)</param>
    public ExtensionContext(
        ExtensionManifest manifest,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        ExtensionEnvironment environment,
        string extensionDirectory,
        HttpClient? apiClient = null)
    {
        Manifest = manifest;
        Services = services;
        Configuration = configuration;
        Logger = logger;
        Environment = environment;
        ExtensionDirectory = extensionDirectory;
        ApiClient = apiClient;
        Data = new Dictionary<string, object>();
    }

    /// <inheritdoc/>
    public ExtensionManifest Manifest { get; }

    /// <inheritdoc/>
    public IServiceProvider Services { get; }

    /// <inheritdoc/>
    public IConfiguration Configuration { get; }

    /// <inheritdoc/>
    public ILogger Logger { get; }

    /// <inheritdoc/>
    public ExtensionEnvironment Environment { get; }

    /// <inheritdoc/>
    public HttpClient? ApiClient { get; }

    /// <inheritdoc/>
    public string ExtensionDirectory { get; }

    /// <inheritdoc/>
    public IDictionary<string, object> Data { get; }
}

/// <summary>
/// Specifies the deployment environment where an extension is running.
/// CRITICAL for distributed deployments where API and Client are separate.
/// </summary>
public enum ExtensionEnvironment
{
    /// <summary>
    /// Extension is running on the API server.
    /// Available services: Database, file system, background workers, etc.
    /// Use for: Backend logic, data processing, external API calls.
    /// </summary>
    Api,

    /// <summary>
    /// Extension is running on the Client (Blazor WebAssembly in browser).
    /// Available services: HttpClient, local storage, Blazor services, etc.
    /// Use for: UI rendering, client-side state, browser interactions.
    /// </summary>
    Client
}

/// <summary>
/// Extension context builder for fluent construction.
/// Used internally by the extension loader.
/// </summary>
public class ExtensionContextBuilder
{
    private ExtensionManifest? _manifest;
    private IServiceProvider? _services;
    private IConfiguration? _configuration;
    private ILogger? _logger;
    private ExtensionEnvironment _environment;
    private string? _extensionDirectory;
    private HttpClient? _apiClient;

    /// <summary>
    /// Sets the extension manifest.
    /// </summary>
    public ExtensionContextBuilder WithManifest(ExtensionManifest manifest)
    {
        _manifest = manifest;
        return this;
    }

    /// <summary>
    /// Sets the service provider.
    /// </summary>
    public ExtensionContextBuilder WithServices(IServiceProvider services)
    {
        _services = services;
        return this;
    }

    /// <summary>
    /// Sets the configuration.
    /// </summary>
    public ExtensionContextBuilder WithConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
        return this;
    }

    /// <summary>
    /// Sets the logger.
    /// </summary>
    public ExtensionContextBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Sets the deployment environment.
    /// </summary>
    public ExtensionContextBuilder WithEnvironment(ExtensionEnvironment environment)
    {
        _environment = environment;
        return this;
    }

    /// <summary>
    /// Sets the extension directory.
    /// </summary>
    public ExtensionContextBuilder WithExtensionDirectory(string directory)
    {
        _extensionDirectory = directory;
        return this;
    }

    /// <summary>
    /// Sets the API client (for Client extensions).
    /// </summary>
    public ExtensionContextBuilder WithApiClient(HttpClient apiClient)
    {
        _apiClient = apiClient;
        return this;
    }

    /// <summary>
    /// Builds the extension context.
    /// </summary>
    /// <returns>Configured extension context</returns>
    /// <exception cref="InvalidOperationException">If required properties are not set</exception>
    public IExtensionContext Build()
    {
        if (_manifest == null)
            throw new InvalidOperationException("Manifest is required");
        if (_services == null)
            throw new InvalidOperationException("Services is required");
        if (_configuration == null)
            throw new InvalidOperationException("Configuration is required");
        if (_logger == null)
            throw new InvalidOperationException("Logger is required");
        if (_extensionDirectory == null)
            throw new InvalidOperationException("ExtensionDirectory is required");

        return new ExtensionContext(
            _manifest,
            _services,
            _configuration,
            _logger,
            _environment,
            _extensionDirectory,
            _apiClient
        );
    }
}
