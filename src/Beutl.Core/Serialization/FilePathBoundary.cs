namespace Beutl;

internal static class PathBoundary
{
    private static readonly StringComparison s_comparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    public static StringComparison Comparison => s_comparison;
}
