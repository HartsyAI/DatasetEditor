namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Where an extension runs. Only the API loads extensions, so the API loads <see cref="Api"/> and
/// <see cref="Both"/> and skips <see cref="Client"/>; nothing loads client extensions today.
/// </summary>
public enum ExtensionDeploymentTarget
{
    /// <summary>The extension has an API assembly and nothing else.</summary>
    Api,

    /// <summary>The extension only has client code, which nothing loads yet.</summary>
    Client,

    /// <summary>The extension has an API assembly and client code.</summary>
    Both
}
