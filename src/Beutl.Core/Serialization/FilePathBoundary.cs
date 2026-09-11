namespace Beutl;

internal static class PathBoundary
{
    private static readonly StringComparison s_comparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    public static StringComparison Comparison => s_comparison;

    public static StringComparer Comparer { get; } = s_comparison == StringComparison.OrdinalIgnoreCase
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool IsPathInsideRoot(string root, string candidate)
        => FilePathComparison.IsSameOrDescendant(root, candidate);

    public static string ResolveDeepestExistingTarget(string path)
        => FilePathComparison.ResolveCanonicalPath(path);

    public static string ResolveExistingPath(string path)
        => FilePathComparison.ResolveCanonicalPath(path);
}
