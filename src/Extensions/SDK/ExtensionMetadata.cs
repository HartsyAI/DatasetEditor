// TODO: Phase 3 - Extension Metadata
//
// Purpose: Define the metadata structure that describes an extension's identity,
// version, capabilities, and requirements. This information is used by the core
// system to validate, load, and manage extensions.
//
// Implementation Plan:
// 1. Define version information class
// 2. Create author/publisher information class
// 3. Define capabilities enumeration
// 4. Create metadata container class
// 5. Implement validation logic
// 6. Add serialization support for JSON/YAML manifests
// 7. Create builder pattern for fluent metadata construction
//
// Dependencies:
// - System.Runtime.Serialization for serialization
// - System.Text.Json or Newtonsoft.Json for JSON support
// - IExtensionValidator interface
// - SemanticVersioning library (or custom implementation)
//
// References:
// - See REFACTOR_PLAN.md Phase 3 - Extension System Infrastructure for details
// - Should follow NuGet package metadata conventions
// - See ExtensionManifest.cs for manifest file integration

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Represents metadata about an extension including version, author, capabilities, etc.
/// This information is typically loaded from the extension's manifest file.
/// </summary>
public class ExtensionMetadata
{
    // TODO: Phase 3 - Add required metadata properties
    // Properties needed:
    // - string Id (unique identifier)
    // - string Name
    // - string Version
    // - string Description
    // - string Author
    // - string License
    // - string Homepage (URI)
    // - string Repository (URI)
    // - IReadOnlyList<string> Tags
    // - IReadOnlyList<string> Categories

    // TODO: Phase 3 - Add capability and requirement metadata
    // Properties needed:
    // - IReadOnlyList<string> ProvidedCapabilities
    // - IReadOnlyList<string> RequiredPermissions
    // - IReadOnlyDictionary<string, string> RequiredDependencies (name -> version)
    // - string MinimumCoreVersion
    // - string MaximumCoreVersion

    // TODO: Phase 3 - Add extension configuration metadata
    // Properties needed:
    // - string EntryPoint (fully qualified type name)
    // - string ConfigurationSchema (JSON schema)
    // - bool IsEnabled (default true)
    // - int LoadOrder (priority)
    // - string[] Platforms (Windows, Linux, macOS)

    // TODO: Phase 3 - Add timestamp and signature metadata
    // Properties needed:
    // - DateTime CreatedDate
    // - DateTime ModifiedDate
    // - string PublisherSignature
    // - bool IsVerified
    // - string CompatibilityHash

    /// <summary>
    /// Validates the metadata to ensure all required fields are present and valid.
    /// </summary>
    /// <returns>true if metadata is valid; otherwise false</returns>
    public bool Validate()
    {
        // TODO: Phase 3 - Implement validation logic
        // Validations needed:
        // - Check required fields are not empty
        // - Validate version format (semantic versioning)
        // - Validate Id format (alphanumeric + dash/underscore)
        // - Check entry point type can be resolved
        // - Validate capability names
        // - Check for circular dependencies

        throw new NotImplementedException("TODO: Phase 3 - Implement metadata validation");
    }

    /// <summary>
    /// Gets validation error messages if the metadata is invalid.
    /// </summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        // TODO: Phase 3 - Collect and return detailed validation errors
        throw new NotImplementedException("TODO: Phase 3 - Implement validation error collection");
    }

    // TODO: Phase 3 - Add builder pattern for fluent construction
    // Methods needed:
    // - static MetadataBuilder CreateBuilder()
    // - MetadataBuilder WithId(string id)
    // - MetadataBuilder WithVersion(string version)
    // - MetadataBuilder WithAuthor(string author)
    // - MetadataBuilder WithCapability(string capability)
    // - ExtensionMetadata Build()
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
