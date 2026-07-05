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
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);

        // A broken symlink resolves to its (missing) target; use that so the boundary check follows
        // where the link would write, not the link's own in-root location.
        FileSystemInfo? target = TryResolveLinkTarget(info);
        return Path.GetFullPath(target?.FullName ?? info.FullName);
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
        catch (IOException)
        {
            // A broken final target throws; fall back to the immediate link target, then the link
            // path itself, so the caller still boundary-checks a resolved location.
            return info.ResolveLinkTarget(returnFinalTarget: false);
        }
    }
}
