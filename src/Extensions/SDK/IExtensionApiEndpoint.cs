// TODO: Phase 3 - Extension API Endpoint Interface
//
// Implemented by: API extensions that expose HTTP endpoints
// Called by: ApiExtensionRegistry during endpoint registration
//
// Purpose: Contract for API endpoint registration in extensions
// Provides a standardized way for extensions to register their HTTP endpoints.
//
// Why This Exists:
// Extensions need a consistent way to expose REST APIs. This interface allows
// extensions to define their endpoints in a structured way, which the loader
// can then register with ASP.NET Core's routing system.
//
// Usage Pattern:
// 1. API extension implements IExtensionApiEndpoint
// 2. GetBasePath() returns the URL prefix (e.g., "/api/extensions/aitools")
// 3. RegisterEndpoints() is called during startup to register routes
// 4. Extension can use minimal APIs or controllers
//
// Distributed Deployment:
// - API side: Endpoints are registered and handle requests
// - Client side: ExtensionApiClient makes HTTP calls to these endpoints
// - Endpoints are accessible from any client (web, mobile, etc.)

using Microsoft.AspNetCore.Routing;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Interface for extensions that expose HTTP API endpoints.
/// Implement this to register RESTful endpoints for your extension.
/// </summary>
public interface IExtensionApiEndpoint
{
    /// <summary>
    /// Gets the base path for all endpoints in this extension.
    /// This should follow the pattern: /api/extensions/{extensionId}
    ///
    /// Example: "/api/extensions/aitools"
    /// </summary>
    /// <returns>Base URL path for extension endpoints</returns>
    string GetBasePath();

    /// <summary>
    /// Registers HTTP endpoints for this extension.
    /// Called during application startup by the extension loader.
    ///
    /// IMPLEMENTATION EXAMPLES:
    ///
    /// Minimal API approach:
    /// <code>
    /// var basePath = GetBasePath();
    /// endpoints.MapPost($"{basePath}/caption", async (CaptionRequest req) =>
    /// {
    ///     // Handle request
    ///     return Results.Ok(response);
    /// });
    /// </code>
    ///
    /// Controller approach:
    /// <code>
    /// endpoints.MapControllers(); // If using [ApiController] classes
    /// </code>
    /// </summary>
    /// <param name="endpoints">Endpoint route builder to register routes</param>
    void RegisterEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>
    /// Gets endpoint metadata for documentation and discovery.
    /// Used to generate API documentation, OpenAPI specs, etc.
    /// </summary>
    /// <returns>List of endpoint descriptors</returns>
    IReadOnlyList<ApiEndpointDescriptor> GetEndpointDescriptors();
}

/// <summary>
/// Base implementation of IExtensionApiEndpoint with common functionality.
/// Extension API handlers can inherit from this for convenience.
/// </summary>
public abstract class ExtensionApiEndpointBase : IExtensionApiEndpoint
{
    private readonly string _extensionId;

    /// <summary>
    /// Initializes a new instance with the specified extension ID.
    /// </summary>
    /// <param name="extensionId">Extension identifier (used in URL path)</param>
    protected ExtensionApiEndpointBase(string extensionId)
    {
        _extensionId = extensionId ?? throw new ArgumentNullException(nameof(extensionId));
    }

    /// <inheritdoc/>
    public virtual string GetBasePath()
    {
        return $"/api/extensions/{_extensionId}";
    }

    /// <inheritdoc/>
    public abstract void RegisterEndpoints(IEndpointRouteBuilder endpoints);

    /// <inheritdoc/>
    public abstract IReadOnlyList<ApiEndpointDescriptor> GetEndpointDescriptors();

    /// <summary>
    /// Helper to create a full endpoint path.
    /// </summary>
    /// <param name="relativePath">Relative path (e.g., "/caption")</param>
    /// <returns>Full path (e.g., "/api/extensions/aitools/caption")</returns>
    protected string GetEndpointPath(string relativePath)
    {
        relativePath = relativePath.TrimStart('/');
        return $"{GetBasePath()}/{relativePath}";
    }
}
