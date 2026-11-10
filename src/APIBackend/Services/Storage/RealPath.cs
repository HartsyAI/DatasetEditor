namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Path containment that follows symlinks in every path component, the managed equivalent of
/// POSIX <c>realpath</c>. Path.GetFullPath only collapses <c>..</c> lexically, and resolving
/// just the final file (FileInfo.ResolveLinkTarget) misses a symlinked directory earlier in the
/// path: <c>{dataset}/linked/secret.jpg</c> with <c>linked -&gt; /etc</c> is not itself a link,
/// so it passed the old check (audit F-2). Comparing the real path of the target against the
/// real path of the root closes that, and still lets a root that is itself reached through a
/// symlink (a mounted volume) serve its own files.
/// </summary>
public static class RealPath
{
    /// <summary>Symlinks followed before giving up, the same bound Linux uses (ELOOP).</summary>
    private const int MaxLinksFollowed = 40;

    /// <summary>
    /// The absolute path with every symlinked component replaced by its target. Components that
    /// don't exist are kept as they are. Throws <see cref="IOException"/> on a symlink loop.
    /// </summary>
    public static string Resolve(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        List<string> pending = SplitComponents(fullPath[root.Length..]);
        string current = root;
        int linksFollowed = 0;

        while (pending.Count > 0)
        {
            string component = pending[0];
            pending.RemoveAt(0);

            if (component == ".")
            {
                continue;
            }
            if (component == "..")
            {
                // current is already fully resolved here, so its parent is the real parent: the
                // same thing the kernel does for ".." inside a link target.
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Join(current, component);
            string? linkTarget = GetLinkTarget(next);
            if (linkTarget is null)
            {
                current = next;
                continue;
            }

            if (++linksFollowed > MaxLinksFollowed)
            {
                throw new IOException($"Too many levels of symbolic links resolving '{path}'.");
            }

            // Re-walk the target's own components (they may contain further links or ".."),
            // followed by whatever was left of the original path.
            List<string> targetComponents;
            if (Path.IsPathRooted(linkTarget))
            {
                string targetRoot = Path.GetPathRoot(linkTarget) ?? string.Empty;
                current = targetRoot;
                targetComponents = SplitComponents(linkTarget[targetRoot.Length..]);
            }
            else
            {
                targetComponents = SplitComponents(linkTarget);
            }
            pending.InsertRange(0, targetComponents);
        }

        return current;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> resolves to a location strictly inside
    /// <paramref name="root"/>, both compared by their real paths.
    /// </summary>
    public static bool IsContainedIn(string root, string candidate)
    {
        string realRoot = Resolve(root);
        string realCandidate = Resolve(candidate);

        // The trailing separator keeps "<root>/data-1234" from matching a sibling
        // "<root>/data-1234EXTRA".
        string prefix = realRoot.EndsWith(Path.DirectorySeparatorChar) ? realRoot : realRoot + Path.DirectorySeparatorChar;
        return realCandidate.StartsWith(prefix, PathComparison);
    }

    /// <summary>
    /// Case-insensitive only where the file system is: on Linux "<c>/data/Foo</c>" and
    /// "<c>/data/foo</c>" are different folders, so an ignore-case match would accept a sibling.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static List<string> SplitComponents(string relativePath)
    {
        return relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    /// <summary>The raw target of a symlink (or junction), or null when the path is not one or doesn't exist.</summary>
    private static string? GetLinkTarget(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        try
        {
            return info.LinkTarget;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Windows reports a missing path as an error rather than "not a link".
            return null;
        }
    }
}
