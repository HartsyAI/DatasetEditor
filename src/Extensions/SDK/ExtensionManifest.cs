using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// An extension's <c>extension.manifest.json</c>: who it is, where it runs and which other
/// extensions it needs.
/// </summary>
/// <remarks>
/// Unknown fields are ignored. Older manifests carried <c>requiredPermissions</c>,
/// <c>apiEndpoints</c>, <c>navigationItems</c> and similar fields that nothing ever read or
/// enforced; they were removed from the model so they no longer read as guarantees (audit X-3,
/// X-5). A manifest that still has them loads, and the fields have no effect.
/// </remarks>
public class ExtensionManifest
{
    /// <summary>File name the loader looks for in each extension folder.</summary>
    public const string ManifestFileName = "extension.manifest.json";

    /// <summary>The only manifest schema version this build understands.</summary>
    public const int ManifestSchemaVersion = 1;

    /// <summary>
    /// The id becomes a file name (<c>{Id}.Api.dll</c>) and a URL segment, so it is limited to
    /// characters that are safe in both. Without this, an id such as <c>../x</c> would make the
    /// loader probe outside the extension's folder.
    /// </summary>
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Schema version of this manifest.</summary>
    public int SchemaVersion { get; set; } = ManifestSchemaVersion;

    /// <summary>Id, name, version and descriptive fields.</summary>
    public required ExtensionMetadata Metadata { get; set; }

    /// <summary>Where the extension runs. The API loads <c>Api</c> and <c>Both</c>.</summary>
    public required ExtensionDeploymentTarget DeploymentTarget { get; set; }

    /// <summary>
    /// Ids of extensions this one needs, mapped to a version range. The API loads dependencies
    /// before their dependents and skips an extension whose dependency is missing, disabled,
    /// part of a cycle or failed to load. The version range is recorded but not checked.
    /// </summary>
    public Dictionary<string, string> Dependencies { get; set; } = new();

    /// <summary>Reads and validates a manifest file.</summary>
    /// <param name="filePath">Full path to an <c>extension.manifest.json</c>.</param>
    /// <returns>The validated manifest.</returns>
    /// <exception cref="InvalidOperationException">The file is not valid JSON or fails validation.</exception>
    public static ExtensionManifest LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Manifest file not found: {filePath}");
        }

        return LoadFromJson(File.ReadAllText(filePath));
    }

    /// <summary>Parses and validates manifest JSON.</summary>
    /// <param name="jsonContent">The manifest's JSON text.</param>
    /// <returns>The validated manifest.</returns>
    /// <exception cref="InvalidOperationException">The text is not valid JSON or fails validation.</exception>
    public static ExtensionManifest LoadFromJson(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new ArgumentException("JSON content cannot be empty", nameof(jsonContent));
        }

        ExtensionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(jsonContent, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse manifest JSON: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException("Failed to deserialize manifest: result was null");
        }

        IReadOnlyList<string> validationErrors = manifest.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Manifest validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, validationErrors)}");
        }

        return manifest;
    }

    /// <summary>Checks the manifest's required fields.</summary>
    /// <returns>Validation errors; empty when the manifest is valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = new();

        if (SchemaVersion != ManifestSchemaVersion)
        {
            errors.Add($"Unsupported schema version: {SchemaVersion}. Expected: {ManifestSchemaVersion}");
        }

        if (Metadata is null)
        {
            errors.Add("Metadata is required");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(Metadata.Id))
        {
            errors.Add("Metadata.Id is required");
        }
        else if (!IdPattern.IsMatch(Metadata.Id))
        {
            errors.Add($"Metadata.Id '{Metadata.Id}' may only contain letters, digits, '.', '-' and '_', and must start with a letter or digit");
        }

        if (string.IsNullOrWhiteSpace(Metadata.Name))
        {
            errors.Add("Metadata.Name is required");
        }

        if (string.IsNullOrWhiteSpace(Metadata.Version))
        {
            errors.Add("Metadata.Version is required");
        }

        if (!Enum.IsDefined(DeploymentTarget))
        {
            errors.Add($"Invalid DeploymentTarget: {DeploymentTarget}");
        }

        foreach (KeyValuePair<string, string> dependency in Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Key))
            {
                errors.Add("Dependency ID cannot be empty");
            }
            if (string.IsNullOrWhiteSpace(dependency.Value))
            {
                errors.Add($"Dependency version for '{dependency.Key}' cannot be empty");
            }
        }

        return errors;
    }
}
