namespace Beutl.AgentToolkit.Common;

// Symlink-aware path resolution shared by the workspace guard and the installer so a textual
// boundary check cannot be bypassed by a symlink/junction under the allowed root.
public static class PathBoundary
{
    // The deepest existing ancestor of <paramref name="absolute"/> with symlinks resolved to their
    // final target, then the not-yet-existing remainder re-appended. A boundary check on the result
    // therefore sees through a symlinked parent instead of trusting the textual path.
    public static string ResolveDeepestExistingTarget(string absolute)
    {
        string? current = absolute;
        var remainder = new Stack<string>();

        // Stop at the deepest node that exists, including a broken symlink (missing target ⇒
        // File/Directory.Exists both false); skipping it would boundary-check the textual path and
        // let a write follow the link outside the root.
        while (!string.IsNullOrEmpty(current) && !PathEntryExists(current))
        {
            string? name = Path.GetFileName(current);
            if (!string.IsNullOrEmpty(name))
            {
                remainder.Push(name);
            }

            current = Path.GetDirectoryName(current);
        }

        if (string.IsNullOrEmpty(current))
        {
            return absolute;
        }

        string resolved = ResolveExistingPath(current);
        while (remainder.Count > 0)
        {
            resolved = Path.Combine(resolved, remainder.Pop());
        }

        return Path.GetFullPath(resolved);
    }

    public static string ResolveExistingPath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        // Resolve every existing component, not just the leaf: an intermediate symlinked directory
        // (e.g. /workspace/link -> /outside) whose leaf exists resolves to the textual in-root path
        // under a leaf-only check, and the boundary check would accept it while a write follows the
        // link outside the root. Walk component by component, following each symlink to its final
        // target, so the result is the real path the filesystem would write to.
        string root = Path.GetPathRoot(path) ?? path;
        var components = new Stack<string>();
        string? current = path;
        while (current is not null && current.Length >= root.Length && !string.Equals(current, root, PathComparison.ForCurrentPlatform))
        {
            string name = Path.GetFileName(current);
            if (!string.IsNullOrEmpty(name))
            {
                components.Push(name);
            }

            current = Path.GetDirectoryName(current);
            if (current is null)
            {
                break;
            }
        }

        string resolved = root;
        while (components.Count > 0)
        {
            string name = components.Pop();
            string candidate = Path.Combine(resolved, name);
            if (TryResolveLinkTarget(candidate, out string? target) && target is not null)
            {
                // Symlink targets may be relative to the link's own directory; resolve them there
                // rather than against the process working directory.
                resolved = Path.IsPathRooted(target)
                    ? Path.GetFullPath(target)
                    : Path.GetFullPath(Path.Combine(resolved, target));
            }
            else
            {
                resolved = Path.GetFullPath(candidate);
            }
        }

        return resolved;
    }

    // Path.Exists follows symlinks; a broken link is missing to it. Query the link entry directly so
    // a dangling symlink still counts as an existing node.
    private static bool PathEntryExists(string path)
        => Path.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static FileSystemInfo? TryResolveLinkTarget(FileSystemInfo info)
    {
        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Reaching here means info IS a link (a non-link returns null without throwing) whose
            // final target could not be followed — broken, permission-denied, or unsupported. Never
            // propagate (this runs in workspace-boundary/installer checks) and never fall back to the
            // link's own in-root path, which would fail OPEN and hide a symlink escape. Instead read
            // the immediate link-target metadata (no filesystem walk) and boundary-check the REAL
            // destination, resolving a relative target against the link's own directory.
            string? immediateTarget = info.LinkTarget;
            if (immediateTarget is null)
            {
                return null;
            }

            string absoluteTarget = Path.IsPathRooted(immediateTarget)
                ? Path.GetFullPath(immediateTarget)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(info.FullName)!, immediateTarget));
            return Directory.Exists(absoluteTarget)
                ? new DirectoryInfo(absoluteTarget)
                : new FileInfo(absoluteTarget);
        }
    }

    private static bool TryResolveLinkTarget(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? target)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        FileSystemInfo? resolved = TryResolveLinkTarget(info);
        target = resolved?.FullName;
        return target is not null;
    }
}
