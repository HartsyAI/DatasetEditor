// TODO: Phase 3 - API Extension Base Class
//
// Called by: API-side extensions (CoreViewer.Api, AITools.Api, Editor.Api, etc.)
// Calls: IExtension interface, ExtensionContext, IServiceCollection, IApplicationBuilder
//
// Purpose: Base implementation for API-side extensions
// Provides common functionality for extensions that run on the API server.
//
// Key Features:
// 1. Automatic API endpoint registration
// 2. Background service registration helpers
// 3. Database migration registration
// 4. Configuration management
// 5. Logging and health monitoring
//
// When to Use:
// - Your extension needs to expose REST API endpoints
// - Your extension performs server-side data processing
// - Your extension needs background workers or scheduled tasks
// - Your extension needs database access
// - Your extension integrates with external APIs (HuggingFace, etc.)
//
// Deployment Note:
// This class is ONLY used on the API server, never on the Client.
// For Client UI, use BaseClientExtension. For both, create separate classes.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Base class for extensions that run on the API server.
/// Provides helper methods for endpoint registration, background services, and configuration.
/// </summary>
public abstract class BaseApiExtension : IExtension
{
    private IExtensionContext? _context;
    private bool _disposed;

    /// <summary>
    /// Gets the extension context (available after InitializeAsync is called).
    /// </summary>
    protected IExtensionContext Context => _context
        ?? throw new InvalidOperationException("Extension not initialized. Call InitializeAsync first.");

    /// <summary>
    /// Gets the logger for this extension.
    /// </summary>
    protected ILogger Logger => Context.Logger;

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    protected IServiceProvider Services => Context.Services;

    /// <inheritdoc/>
    public abstract ExtensionManifest GetManifest();

    /// <inheritdoc/>
    public virtual async Task InitializeAsync(IExtensionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        Logger.LogInformation(
            "Initializing API extension: {ExtensionId} v{Version}",
            context.Manifest.Metadata.Id,
            context.Manifest.Metadata.Version);

        // Call derived class initialization
        await OnInitializeAsync();

        Logger.LogInformation(
            "API extension initialized successfully: {ExtensionId}",
            context.Manifest.Metadata.Id);
    }

    /// <summary>
    /// Override this method to perform custom initialization logic.
    /// Called during InitializeAsync after context is set up.
    /// </summary>
    protected virtual Task OnInitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Derived classes override this to register their services
        Logger?.LogDebug("Configuring services for {ExtensionId}", GetManifest().Metadata.Id);
    }

    /// <inheritdoc/>
    public virtual void ConfigureApp(IApplicationBuilder app)
    {
        // Register API endpoints from manifest
        if (app is IEndpointRouteBuilder endpoints)
        {
            RegisterEndpoints(endpoints);
        }

        // Call derived class app configuration
        OnConfigureApp(app);

        Logger?.LogDebug(
            "Configured application pipeline for {ExtensionId}",
            GetManifest().Metadata.Id);
    }

    /// <summary>
    /// Override this method to configure the application pipeline.
    /// Called during ConfigureApp after endpoints are registered.
    /// </summary>
    /// <param name="app">Application builder</param>
    protected virtual void OnConfigureApp(IApplicationBuilder app)
    {
        // Derived classes can override to add middleware
    }

    /// <summary>
    /// Registers API endpoints defined in the extension manifest.
    /// Override this to customize endpoint registration.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder</param>
    protected virtual void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        var manifest = GetManifest();

        // TODO: Phase 3 - Implement automatic endpoint registration
        // For each ApiEndpointDescriptor in manifest.ApiEndpoints:
        // 1. Resolve handler type from HandlerType property
        // 2. Register endpoint with specified Method and Route
        // 3. Apply authentication if RequiresAuth is true
        // 4. Add endpoint to route builder

        Logger.LogDebug(
            "Registering {Count} API endpoints for {ExtensionId}",
            manifest.ApiEndpoints.Count,
            manifest.Metadata.Id);
    }

    /// <summary>
    /// Helper method to register a background service.
    /// </summary>
    /// <typeparam name="TService">Background service type (must implement IHostedService)</typeparam>
    /// <param name="services">Service collection</param>
    protected void AddBackgroundService<TService>(IServiceCollection services)
        where TService : class, Microsoft.Extensions.Hosting.IHostedService
    {
        services.AddHostedService<TService>();
        Logger?.LogDebug("Registered background service: {ServiceType}", typeof(TService).Name);
    }

    /// <summary>
    /// Helper method to register a scoped service.
    /// </summary>
    protected void AddScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddScoped<TService, TImplementation>();
    }

    /// <summary>
    /// Helper method to register a singleton service.
    /// </summary>
    protected void AddSingleton<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TService, TImplementation>();
    }

    /// <summary>
    /// Helper method to register a transient service.
    /// </summary>
    protected void AddTransient<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddTransient<TService, TImplementation>();
    }

    /// <inheritdoc/>
    public virtual async Task<bool> ValidateAsync()
    {
        try
        {
            Logger.LogDebug("Validating extension: {ExtensionId}", GetManifest().Metadata.Id);

            // Call custom validation
            var isValid = await OnValidateAsync();

            if (isValid)
            {
                Logger.LogInformation("Extension validation successful: {ExtensionId}", GetManifest().Metadata.Id);
            }
            else
            {
                Logger.LogWarning("Extension validation failed: {ExtensionId}", GetManifest().Metadata.Id);
            }

            return isValid;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during extension validation: {ExtensionId}", GetManifest().Metadata.Id);
            return false;
        }
    }

    /// <summary>
    /// Override this to perform custom validation logic.
    /// </summary>
    protected virtual Task<bool> OnValidateAsync()
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public virtual async Task<ExtensionHealthStatus> GetHealthAsync()
    {
        try
        {
            // Call custom health check
            var health = await OnGetHealthAsync();
            return health;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during health check: {ExtensionId}", GetManifest().Metadata.Id);
            return new ExtensionHealthStatus
            {
                Health = ExtensionHealth.Unhealthy,
                Message = $"Health check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["Exception"] = ex.ToString()
                }
            };
        }
    }

    /// <summary>
    /// Override this to perform custom health checks.
    /// Default implementation returns Healthy.
    /// </summary>
    protected virtual Task<ExtensionHealthStatus> OnGetHealthAsync()
    {
        return Task.FromResult(new ExtensionHealthStatus
        {
            Health = ExtensionHealth.Healthy,
            Message = "Extension is healthy"
        });
    }

    /// <summary>
    /// Disposes resources used by the extension.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        Logger?.LogDebug("Disposing extension: {ExtensionId}", GetManifest()?.Metadata?.Id);

        OnDispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override this to clean up extension-specific resources.
    /// </summary>
    protected virtual void OnDispose()
    {
        // Derived classes can override to clean up resources
    }
}
