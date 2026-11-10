using System.Reflection;
using System.Runtime.Loader;

namespace DatasetStudio.APIBackend.Services.Extensions;

/// <summary>
/// The assembly load context for one extension. The API's own assemblies come from the API; any
/// other dependency resolves from the extension's folder through its <c>.deps.json</c>.
/// </summary>
/// <remarks>
/// Audit X-2: extensions were loaded into a bare <see cref="AssemblyLoadContext"/> with no
/// resolver, which only looks in the API's own dependency list, so the first use of a library
/// that shipped only with the extension threw FileNotFoundException.
/// <para>
/// The API wins for every assembly it has. This keeps one copy of <c>Extensions.SDK</c> (an
/// extension's private copy would define a different <c>IApiExtension</c> type, and the loader
/// would find no implementation) and of ASP.NET Core, Core and DTO, which is why the duplicate
/// copies a publish puts next to an extension are harmless (audit R-10). An extension that needs
/// a different version of a library the API already has gets the API's version.
/// </para>
/// </remarks>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    /// <summary>Simple names of every assembly the API itself resolves (its trusted platform assemblies).</summary>
    private static readonly HashSet<string> HostAssemblyNames = ReadHostAssemblyNames();

    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>Creates the context for the extension whose API assembly is <paramref name="mainAssemblyPath"/>.</summary>
    /// <param name="extensionId">Extension id, used as the context name.</param>
    /// <param name="mainAssemblyPath">Full path to the extension's <c>{Id}.Api.dll</c>.</param>
    public ExtensionLoadContext(string extensionId, string mainAssemblyPath)
        : base($"Extension:{extensionId}")
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning null defers to the default context, i.e. the API's copy.
        if (assemblyName.Name is not null && HostAssemblyNames.Contains(assemblyName.Name))
        {
            return null;
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc/>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static HashSet<string> ReadHostAssemblyNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                names.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        return names;
    }
}
