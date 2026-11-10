using DatasetStudio.Extensions.SDK;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// An extension whose API assembly was loaded and instantiated.
/// </summary>
/// <param name="Manifest">The extension's manifest.</param>
/// <param name="Directory">The folder the manifest was found in.</param>
/// <param name="Instance">The extension's <see cref="IApiExtension"/> implementation.</param>
public sealed record LoadedApiExtension(ExtensionManifest Manifest, string Directory, IApiExtension Instance);
