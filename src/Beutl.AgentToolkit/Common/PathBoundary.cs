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

        while (!string.IsNullOrEmpty(current)
               && !File.Exists(current)
               && !Directory.Exists(current))
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

        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        return Path.GetFullPath(target?.FullName ?? info.FullName);
    }
}
