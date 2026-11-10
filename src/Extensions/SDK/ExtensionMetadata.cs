using System.Text.Json.Serialization;

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// The <c>metadata</c> block of an extension manifest.
/// </summary>
public class ExtensionMetadata
{
    /// <summary>
    /// Unique identifier. The API assembly must be named <c>{Id}.Api.dll</c>, and the extension's
    /// routes live under <c>/api/extensions/{id}</c> in lower case.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Version, logged when the extension loads.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>What the extension does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Author or publisher.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>License identifier, for example "MIT".</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }
}
