namespace Beutl;

internal static class PathBoundary
{
    private static readonly StringComparison s_comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static StringComparison Comparison => s_comparison;

    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool IsPathInsideRoot(string root, string candidate)
    {
        string prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(candidate, root, s_comparison)
               || candidate.StartsWith(prefix, s_comparison);
    }

    public static string ResolveDeepestExistingTarget(string path)
    {
        string absolute = Path.GetFullPath(path);
        string? current = absolute;
        var remainder = new Stack<string>();
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
        string absolute = Path.GetFullPath(path);
        string root = Path.GetPathRoot(absolute) ?? absolute;
        var components = new Stack<string>();
        string? current = absolute;
        while (current is not null
               && current.Length >= root.Length
               && !string.Equals(current, root, s_comparison))
        {
            string name = Path.GetFileName(current);
            if (!string.IsNullOrEmpty(name))
            {
                components.Push(name);
            }

            current = Path.GetDirectoryName(current);
        }

        string resolved = root;
        while (components.Count > 0)
        {
            string candidate = Path.Combine(resolved, components.Pop());
            if (TryResolveLinkTarget(candidate, out string? target))
            {
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

    private static bool PathEntryExists(string path)
        => Path.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static FileSystemInfo? TryResolveLinkTarget(FileSystemInfo info)
    {
        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            try
            {
                string? immediateTarget = info.LinkTarget;
                string? baseDirectory = Path.GetDirectoryName(info.FullName);
                if (immediateTarget is null || baseDirectory is null)
                {
                    return null;
                }

                string absoluteTarget = Path.IsPathRooted(immediateTarget)
                    ? Path.GetFullPath(immediateTarget)
                    : Path.GetFullPath(Path.Combine(baseDirectory, immediateTarget));
                return Directory.Exists(absoluteTarget)
                    ? new DirectoryInfo(absoluteTarget)
                    : new FileInfo(absoluteTarget);
            }
            catch (Exception fallbackEx) when (fallbackEx is IOException
                                                   or UnauthorizedAccessException
                                                   or NotSupportedException
                                                   or ArgumentException)
            {
                return null;
            }
        }
    }

    private static bool TryResolveLinkTarget(
        string path,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? target)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        FileSystemInfo? resolved = TryResolveLinkTarget(info);
        target = resolved?.FullName;
        return target is not null;
    }
}
