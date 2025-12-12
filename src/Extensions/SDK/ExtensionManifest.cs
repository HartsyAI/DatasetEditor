using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Handles reading, parsing, validating, and writing extension manifest files.
/// Manifest files are JSON files named "extension.manifest.json" in extension directories.
/// </summary>
public class ExtensionManifest
{
    /// <summary>
    /// Standard filename for extension manifests.
    /// </summary>
    public const string ManifestFileName = "extension.manifest.json";

    /// <summary>
    /// Current version of the manifest schema.
    /// </summary>
    public const int ManifestSchemaVersion = 1;

    /// <summary>
    /// Schema version of this manifest (for future migration support).
    /// </summary>
    public int SchemaVersion { get; set; } = ManifestSchemaVersion;

    /// <summary>
    /// Extension metadata (id, name, version, author, etc.).
    /// </summary>
    public required ExtensionMetadata Metadata { get; set; }

    /// <summary>
    /// Specifies where this extension runs: "api", "client", or "both".
    /// CRITICAL for distributed deployments where API and Client are on different servers.
    /// </summary>
    public required ExtensionDeploymentTarget DeploymentTarget { get; set; }

    /// <summary>
    /// Dependencies on other extensions (extensionId -> version requirement).
    /// Format: "extensionId": ">=1.0.0" or "extensionId": "^2.0.0"
    /// </summary>
    public Dictionary<string, string> Dependencies { get; set; } = new();

    /// <summary>
    /// Required permissions for this extension.
    /// e.g., "filesystem.read", "api.datasets.write", "ai.huggingface"
    /// </summary>
    public List<string> RequiredPermissions { get; set; } = new();

    /// <summary>
    /// API endpoints registered by this extension (only for API-side extensions).
    /// e.g., "/api/extensions/aitools/caption", "/api/extensions/editor/batch"
    /// </summary>
    public List<ApiEndpointDescriptor> ApiEndpoints { get; set; } = new();

    /// <summary>
    /// Blazor components registered by this extension (only for Client-side extensions).
    /// Maps component name to fully qualified type name.
    /// </summary>
    public Dictionary<string, string> BlazorComponents { get; set; } = new();

    /// <summary>
    /// Navigation menu items to register (only for Client-side extensions).
    /// </summary>
    public List<NavigationMenuItem> NavigationItems { get; set; } = new();

    /// <summary>
    /// Background workers/services registered by this extension (API-side only).
    /// </summary>
    public List<BackgroundWorkerDescriptor> BackgroundWorkers { get; set; } = new();

    /// <summary>
    /// Database migrations provided by this extension (API-side only).
    /// </summary>
    public List<string> DatabaseMigrations { get; set; } = new();

    /// <summary>
    /// Configuration schema for this extension (JSON Schema format).
    /// </summary>
    public string? ConfigurationSchema { get; set; }

    /// <summary>
    /// Default configuration values.
    /// </summary>
    public Dictionary<string, object> DefaultConfiguration { get; set; } = new();

    // Manifest location and file tracking
    /// <summary>
    /// Directory path where this extension is located.
    /// </summary>
    public string? DirectoryPath { get; set; }

    /// <summary>
    /// Full path to the manifest file.
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Last modification time of the manifest file.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// SHA256 hash of the manifest file (for caching and change detection).
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Loads a manifest from the specified directory.
    /// </summary>
    /// <param name="directoryPath">Path to the extension directory containing extension.manifest.json</param>
    /// <returns>Loaded manifest or null if manifest not found</returns>
    public static ExtensionManifest? LoadFromDirectory(string directoryPath)
    {
        // TODO: Phase 3 - Implement manifest loading
        // Steps:
        // 1. Validate directory exists
        // 2. Check for extension.manifest.json file
        // 3. Read file contents
        // 4. Parse JSON to manifest object
        // 5. Validate manifest
        // 6. Return populated ExtensionManifest instance

        throw new NotImplementedException("TODO: Phase 3 - Implement manifest loading from directory");
    }

    /// <summary>
    /// Loads a manifest from a file path.
    /// </summary>
    /// <param name="filePath">Full path to the extension.manifest.json file</param>
    /// <returns>Loaded manifest</returns>
    public static ExtensionManifest LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Manifest file not found: {filePath}");
        }

        var jsonContent = File.ReadAllText(filePath);
        var manifest = LoadFromJson(jsonContent);

        // Set file metadata
        manifest.ManifestPath = filePath;
        manifest.DirectoryPath = Path.GetDirectoryName(filePath);
        manifest.LastModified = File.GetLastWriteTimeUtc(filePath);
        manifest.FileHash = ComputeFileHash(filePath);

        return manifest;
    }

    /// <summary>
    /// Loads a manifest from JSON string content.
    /// </summary>
    /// <param name="jsonContent">JSON content of the manifest</param>
    /// <returns>Loaded manifest</returns>
    public static ExtensionManifest LoadFromJson(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new ArgumentException("JSON content cannot be empty", nameof(jsonContent));
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(jsonContent, options);

            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to deserialize manifest: result was null");
            }

            // Validate the manifest
            var validationErrors = manifest.Validate();
            if (validationErrors.Count > 0)
            {
                var errors = string.Join(Environment.NewLine, validationErrors);
                throw new InvalidOperationException($"Manifest validation failed:{Environment.NewLine}{errors}");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse manifest JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates the manifest structure and content.
    /// </summary>
    /// <returns>List of validation errors (empty if valid)</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // Validate schema version
        if (SchemaVersion != ManifestSchemaVersion)
        {
            errors.Add($"Unsupported schema version: {SchemaVersion}. Expected: {ManifestSchemaVersion}");
        }

        // Validate metadata
        if (Metadata == null)
        {
            errors.Add("Metadata is required");
            return errors; // Can't continue without metadata
        }

        if (string.IsNullOrWhiteSpace(Metadata.Id))
        {
            errors.Add("Metadata.Id is required");
        }

        if (string.IsNullOrWhiteSpace(Metadata.Name))
        {
            errors.Add("Metadata.Name is required");
        }

        if (string.IsNullOrWhiteSpace(Metadata.Version))
        {
            errors.Add("Metadata.Version is required");
        }

        // Validate deployment target
        if (!Enum.IsDefined(typeof(ExtensionDeploymentTarget), DeploymentTarget))
        {
            errors.Add($"Invalid DeploymentTarget: {DeploymentTarget}");
        }

        // Validate dependencies
        foreach (var (depId, depVersion) in Dependencies)
        {
            if (string.IsNullOrWhiteSpace(depId))
            {
                errors.Add("Dependency ID cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(depVersion))
            {
                errors.Add($"Dependency version for '{depId}' cannot be empty");
            }
        }

        // Validate API endpoints
        foreach (var endpoint in ApiEndpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Method))
            {
                errors.Add("API endpoint method cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(endpoint.Route))
            {
                errors.Add("API endpoint route cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(endpoint.HandlerType))
            {
                errors.Add($"API endpoint handler type cannot be empty for route: {endpoint.Route}");
            }
        }

        // Validate navigation items
        foreach (var navItem in NavigationItems)
        {
            if (string.IsNullOrWhiteSpace(navItem.Text))
            {
                errors.Add("Navigation item text cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(navItem.Route))
            {
                errors.Add($"Navigation item route cannot be empty for: {navItem.Text}");
            }
        }

        // Validate background workers
        foreach (var worker in BackgroundWorkers)
        {
            if (string.IsNullOrWhiteSpace(worker.Id))
            {
                errors.Add("Background worker ID cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(worker.TypeName))
            {
                errors.Add($"Background worker type name cannot be empty for: {worker.Id}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Saves the manifest to a JSON file.
    /// </summary>
    /// <param name="filePath">Path where manifest should be saved</param>
    public void SaveToFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = ToJson(indented: true);
        File.WriteAllText(filePath, json);

        // Update metadata
        ManifestPath = filePath;
        DirectoryPath = Path.GetDirectoryName(filePath);
        LastModified = File.GetLastWriteTimeUtc(filePath);
        FileHash = ComputeFileHash(filePath);
    }

    /// <summary>
    /// Converts the manifest to JSON string.
    /// </summary>
    /// <param name="indented">Whether to format with indentation</param>
    /// <returns>JSON representation of the manifest</returns>
    public string ToJson(bool indented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Computes SHA256 hash of a file.
    /// </summary>
    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

/// <summary>
/// Describes a capability provided by an extension.
/// </summary>
public class ExtensionCapabilityDescriptor
{
    // TODO: Phase 3 - Add capability descriptor properties
    // Properties needed:
    // - string Name (unique capability identifier)
    // - string DisplayName
    // - string Description
    // - string Category
    // - IReadOnlyList<string> Parameters
    // - string Version
    // - bool IsPublic
}

/// <summary>
/// Validator for extension manifest files.
/// </summary>
public class ManifestValidator
{
    // TODO: Phase 3 - Implement manifest schema validation
    // Methods needed:
    // - bool ValidateSchema(string jsonContent)
    // - IReadOnlyList<string> GetSchemaValidationErrors()
    // - bool ValidateManifestStructure(ExtensionManifest manifest)
    // - bool ValidateCapabilities(IReadOnlyList<ExtensionCapabilityDescriptor> capabilities)
    // - bool ValidateDependencies(IReadOnlyDictionary<string, string> dependencies)

    // TODO: Phase 3 - Add detailed error reporting
    // Methods needed:
    // - ManifestValidationResult Validate(ExtensionManifest manifest)
    // Returns detailed error/warning information with line numbers and suggestions
}

/// <summary>
/// Result of manifest validation with detailed information.
/// </summary>
public class ManifestValidationResult
{
    // TODO: Phase 3 - Add validation result properties
    // Properties needed:
    // - bool IsValid
    // - IReadOnlyList<ManifestValidationError> Errors
    // - IReadOnlyList<ManifestValidationWarning> Warnings
    // - string SummaryMessage
}

/// <summary>
/// Specifies where an extension runs - critical for distributed deployments.
/// </summary>
public enum ExtensionDeploymentTarget
{
    /// <summary>
    /// Extension runs only on the API server.
    /// Use for: background workers, database operations, file system access, AI processing.
    /// </summary>
    Api,

    /// <summary>
    /// Extension runs only on the Client (Blazor WebAssembly).
    /// Use for: UI components, client-side rendering, browser interactions.
    /// </summary>
    Client,

    /// <summary>
    /// Extension has both API and Client components.
    /// Use for: full-stack features requiring server logic and UI.
    /// Example: AITools has API for HuggingFace calls, Client for UI.
    /// </summary>
    Both
}

/// <summary>
/// Describes an API endpoint registered by an extension.
/// </summary>
public class ApiEndpointDescriptor
{
    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// </summary>
    public required string Method { get; set; }

    /// <summary>
    /// Route pattern (e.g., "/api/extensions/aitools/caption").
    /// </summary>
    public required string Route { get; set; }

    /// <summary>
    /// Handler type name (fully qualified).
    /// </summary>
    public required string HandlerType { get; set; }

    /// <summary>
    /// Brief description of what this endpoint does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this endpoint requires authentication.
    /// </summary>
    public bool RequiresAuth { get; set; } = false;
}

/// <summary>
/// Describes a navigation menu item registered by a client extension.
/// </summary>
public class NavigationMenuItem
{
    /// <summary>
    /// Display text for the menu item.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Route/URL to navigate to.
    /// </summary>
    public required string Route { get; set; }

    /// <summary>
    /// Icon name (MudBlazor icon or custom).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Display order (lower numbers appear first).
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// Parent menu item (for sub-menus).
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Required permission to see this menu item.
    /// </summary>
    public string? RequiredPermission { get; set; }
}

/// <summary>
/// Describes a background worker/service registered by an API extension.
/// </summary>
public class BackgroundWorkerDescriptor
{
    /// <summary>
    /// Unique identifier for this worker.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Worker type name (fully qualified, must implement IHostedService).
    /// </summary>
    public required string TypeName { get; set; }

    /// <summary>
    /// Brief description of what this worker does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether to start this worker automatically on startup.
    /// </summary>
    public bool AutoStart { get; set; } = true;
}
