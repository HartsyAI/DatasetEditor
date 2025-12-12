// TODO: Phase 3 - Client Extension Base Class
//
// Called by: Client-side extensions (CoreViewer.Client, AITools.Client, Editor.Client, etc.)
// Calls: IExtension interface, ExtensionContext, IServiceCollection, HttpClient
//
// Purpose: Base implementation for Client-side extensions (Blazor WebAssembly)
// Provides common functionality for extensions that run in the browser.
//
// Key Features:
// 1. Blazor component registration helpers
// 2. Navigation menu item registration
// 3. HTTP client configuration for API calls
// 4. Client-side service registration
// 5. Local storage and browser API access
//
// When to Use:
// - Your extension needs UI components (Blazor pages/components)
// - Your extension needs to render data in the browser
// - Your extension needs client-side state management
// - Your extension needs to interact with browser APIs
// - Your extension needs to call backend API endpoints
//
// Deployment Note:
// This class is ONLY used on the Client (Blazor WASM), never on the API server.
// For API logic, use BaseApiExtension. For both, create separate classes.
//
// Communication with API:
// Use Context.ApiClient to make HTTP calls to your extension's API endpoints.
// The HttpClient is pre-configured with the API base URL from appsettings.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Base class for extensions that run on the Client (Blazor WebAssembly).
/// Provides helper methods for component registration, navigation, and API communication.
/// </summary>
public abstract class BaseClientExtension : IExtension
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

    /// <summary>
    /// Gets the HTTP client for calling backend API endpoints.
    /// Pre-configured with API base URL and authentication.
    /// </summary>
    protected HttpClient ApiClient => Context.ApiClient
        ?? throw new InvalidOperationException("ApiClient not available in context");

    /// <inheritdoc/>
    public abstract ExtensionManifest GetManifest();

    /// <inheritdoc/>
    public virtual async Task InitializeAsync(IExtensionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        Logger.LogInformation(
            "Initializing Client extension: {ExtensionId} v{Version}",
            context.Manifest.Metadata.Id,
            context.Manifest.Metadata.Version);

        // Call derived class initialization
        await OnInitializeAsync();

        Logger.LogInformation(
            "Client extension initialized successfully: {ExtensionId}",
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
        // Not used in Blazor WASM (no middleware pipeline)
        // Client extensions can leave this empty
    }

    /// <summary>
    /// Registers Blazor components defined in the extension manifest.
    /// This is called automatically by the extension loader.
    /// Override to customize component registration.
    /// </summary>
    public virtual void RegisterComponents()
    {
        var manifest = GetManifest();

        // TODO: Phase 3 - Implement automatic component registration
        // For each component in manifest.BlazorComponents:
        // 1. Resolve component type from fully qualified name
        // 2. Register with Blazor routing system
        // 3. Make component discoverable by the UI

        Logger.LogDebug(
            "Registering {Count} Blazor components for {ExtensionId}",
            manifest.BlazorComponents.Count,
            manifest.Metadata.Id);
    }

    /// <summary>
    /// Registers navigation menu items defined in the extension manifest.
    /// This is called automatically by the extension loader.
    /// Override to customize navigation registration.
    /// </summary>
    public virtual void RegisterNavigation()
    {
        var manifest = GetManifest();

        // TODO: Phase 3 - Implement automatic navigation registration
        // For each NavigationMenuItem in manifest.NavigationItems:
        // 1. Add to navigation menu service
        // 2. Apply ordering and hierarchy
        // 3. Check permissions if specified

        Logger.LogDebug(
            "Registering {Count} navigation items for {ExtensionId}",
            manifest.NavigationItems.Count,
            manifest.Metadata.Id);
    }

    /// <summary>
    /// Helper method to make a GET request to the extension's API.
    /// </summary>
    /// <typeparam name="TResponse">Response type</typeparam>
    /// <param name="endpoint">API endpoint path (e.g., "/caption")</param>
    /// <returns>Deserialized response</returns>
    protected async Task<TResponse?> GetAsync<TResponse>(string endpoint)
    {
        var extensionId = GetManifest().Metadata.Id;
        var url = $"/api/extensions/{extensionId}{endpoint}";

        Logger.LogDebug("GET {Url}", url);

        try
        {
            return await ApiClient.GetFromJsonAsync<TResponse>(url);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling GET {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Helper method to make a POST request to the extension's API.
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TResponse">Response type</typeparam>
    /// <param name="endpoint">API endpoint path</param>
    /// <param name="request">Request payload</param>
    /// <returns>Deserialized response</returns>
    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest request)
    {
        var extensionId = GetManifest().Metadata.Id;
        var url = $"/api/extensions/{extensionId}{endpoint}";

        Logger.LogDebug("POST {Url}", url);

        try
        {
            var response = await ApiClient.PostAsJsonAsync(url, request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling POST {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Helper method to make a PUT request to the extension's API.
    /// </summary>
    protected async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest request)
    {
        var extensionId = GetManifest().Metadata.Id;
        var url = $"/api/extensions/{extensionId}{endpoint}";

        Logger.LogDebug("PUT {Url}", url);

        try
        {
            var response = await ApiClient.PutAsJsonAsync(url, request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling PUT {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Helper method to make a DELETE request to the extension's API.
    /// </summary>
    protected async Task<bool> DeleteAsync(string endpoint)
    {
        var extensionId = GetManifest().Metadata.Id;
        var url = $"/api/extensions/{extensionId}{endpoint}";

        Logger.LogDebug("DELETE {Url}", url);

        try
        {
            var response = await ApiClient.DeleteAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling DELETE {Url}", url);
            throw;
        }
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
            // For client extensions, we can check API connectivity
            var health = await OnGetHealthAsync();

            // Try pinging the API to verify connectivity
            try
            {
                var extensionId = GetManifest().Metadata.Id;
                var healthUrl = $"/api/extensions/{extensionId}/health";
                var response = await ApiClient.GetAsync(healthUrl);

                if (!response.IsSuccessStatusCode)
                {
                    health.Health = ExtensionHealth.Degraded;
                    health.Message = $"API health check returned {response.StatusCode}";
                }
            }
            catch
            {
                // API health endpoint not available - not critical
            }

            return health;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during health check: {ExtensionId}", GetManifest().Metadata.Id);
            return new ExtensionHealthStatus
            {
                Health = ExtensionHealth.Unhealthy,
                Message = $"Health check failed: {ex.Message}"
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
