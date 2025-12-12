// TODO: Phase 3 - Extension Interface
//
// Called by: ExtensionLoader (API and Client) when discovering extensions
// Calls: Nothing (implemented by concrete extensions)
//
// Purpose: Base contract for all Dataset Studio extensions
// This interface defines the lifecycle methods and required operations that
// all extensions must implement, regardless of deployment target (API/Client/Both).
//
// Key Design Principles:
// 1. Extensions must be self-describing (via GetManifest)
// 2. Extensions must support async initialization
// 3. Extensions must configure their own DI services
// 4. Extensions must be disposable for cleanup
//
// Deployment Considerations:
// - API extensions: InitializeAsync called during API server startup
// - Client extensions: InitializeAsync called during Blazor app startup
// - Both: InitializeAsync called on both API and Client (ensure idempotent!)
//
// Implementation Notes:
// - Extensions should inherit from BaseApiExtension or BaseClientExtension
// - Direct IExtension implementation is allowed but discouraged
// - GetManifest() should return a cached instance (called frequently)

using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Base interface that all Dataset Studio extensions must implement.
/// Defines the core lifecycle and configuration methods for extensions.
/// </summary>
public interface IExtension : IDisposable
{
    /// <summary>
    /// Gets the extension manifest containing metadata and capabilities.
    /// This method is called frequently - implementations should cache the result.
    /// </summary>
    /// <returns>Extension manifest with complete metadata</returns>
    ExtensionManifest GetManifest();

    /// <summary>
    /// Called once when the extension is first loaded.
    /// Use this for one-time initialization logic, resource allocation, etc.
    ///
    /// IMPORTANT FOR DISTRIBUTED DEPLOYMENTS:
    /// - API extensions: Initialize server-side resources, DB connections, file watchers
    /// - Client extensions: Initialize client-side caches, local storage, UI state
    /// - Both: This method is called on BOTH sides - ensure initialization is idempotent!
    /// </summary>
    /// <param name="context">Extension context with configuration, services, and logger</param>
    /// <returns>Task representing the initialization operation</returns>
    Task InitializeAsync(IExtensionContext context);

    /// <summary>
    /// Configures dependency injection services for this extension.
    /// Called during application startup, before InitializeAsync().
    ///
    /// DEPLOYMENT NOTES:
    /// - API extensions: Register services like HttpClient, repositories, background workers
    /// - Client extensions: Register Blazor services, view models, API clients
    /// - Both: Called on both API and Client - register appropriate services for each side
    /// </summary>
    /// <param name="services">Service collection to register services into</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Configures the application middleware pipeline (API extensions only).
    /// Called after services are configured but before the app runs.
    ///
    /// USE CASES:
    /// - Register minimal API endpoints
    /// - Add custom middleware
    /// - Configure request pipeline
    /// - Register static file directories
    ///
    /// NOTE: Client extensions can leave this empty (not used in Blazor WASM).
    /// </summary>
    /// <param name="app">Application builder to configure middleware</param>
    void ConfigureApp(IApplicationBuilder app);

    /// <summary>
    /// Validates that the extension is properly configured and can run.
    /// Called after InitializeAsync() and before the extension is activated.
    ///
    /// VALIDATION EXAMPLES:
    /// - Check required configuration values are present
    /// - Verify API keys are valid
    /// - Ensure required files/directories exist
    /// - Validate dependency versions
    /// </summary>
    /// <returns>True if extension is valid and ready; false otherwise</returns>
    Task<bool> ValidateAsync();

    /// <summary>
    /// Gets the current health status of the extension.
    /// Used for monitoring and diagnostics.
    /// </summary>
    /// <returns>Extension health status</returns>
    Task<ExtensionHealthStatus> GetHealthAsync();
}

/// <summary>
/// Extension health status for monitoring and diagnostics.
/// </summary>
public class ExtensionHealthStatus
{
    /// <summary>
    /// Overall health state.
    /// </summary>
    public required ExtensionHealth Health { get; set; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Additional diagnostic details (for debugging).
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }

    /// <summary>
    /// Timestamp when status was checked.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Extension health states.
/// </summary>
public enum ExtensionHealth
{
    /// <summary>
    /// Extension is healthy and operating normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// Extension is running but with degraded functionality.
    /// Example: API calls are slow, cache is full, non-critical service is down.
    /// </summary>
    Degraded,

    /// <summary>
    /// Extension is not functioning correctly.
    /// Example: Database unreachable, required API key missing, critical error.
    /// </summary>
    Unhealthy
}
