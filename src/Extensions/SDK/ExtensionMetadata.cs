using System.Text.Json.Serialization;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Represents metadata about an extension including version, author, capabilities, etc.
/// This information is typically loaded from the extension's manifest file.
/// </summary>
public class ExtensionMetadata
{
    /// <summary>
    /// Unique identifier for the extension (e.g., "dataset-studio.core-viewer").
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Display name of the extension (e.g., "Core Viewer").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Semantic version of the extension (e.g., "1.0.0").
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>
    /// Description of what the extension does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Author or publisher of the extension.
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// License identifier (e.g., "MIT", "Apache-2.0").
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Homepage URL for the extension.
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// Repository URL (e.g., GitHub, GitLab).
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Tags for categorization and search.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Categories this extension belongs to.
    /// </summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Icon path or URL for the extension.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Minimum core version required (e.g., "1.0.0").
    /// </summary>
    [JsonPropertyName("minimumCoreVersion")]
    public string? MinimumCoreVersion { get; set; }

    /// <summary>
    /// Maximum compatible core version.
    /// </summary>
    [JsonPropertyName("maximumCoreVersion")]
    public string? MaximumCoreVersion { get; set; }

    /// <summary>
    /// Validates the metadata to ensure all required fields are present and valid.
    /// </summary>
    /// <returns>true if metadata is valid; otherwise false</returns>
    public bool Validate()
    {
        return GetValidationErrors().Count == 0;
    }

    /// <summary>
    /// Gets validation error messages if the metadata is invalid.
    /// </summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("Id is required");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            errors.Add("Version is required");
        }

        return errors;
    }
}

/// <summary>
/// Represents version information for an extension.
/// </summary>
public class ExtensionVersion
{
    // TODO: Phase 3 - Implement semantic versioning
    // Properties needed:
    // - int Major
    // - int Minor
    // - int Patch
    // - string PreRelease (beta, alpha, rc)
    // - string Metadata (build info)

    // Methods needed:
    // - bool IsCompatibleWith(string coreVersion)
    // - int CompareTo(ExtensionVersion other)
    // - bool IsPrereleaseVersion
    // - string ToString() (returns 1.2.3-beta+build)
}

/// <summary>
/// Represents author/publisher information for an extension.
/// </summary>
public class ExtensionPublisher
{
    // TODO: Phase 3 - Add publisher information
    // Properties needed:
    // - string Name
    // - string Email
    // - string Website
    // - string PublisherId (for verification)
}
