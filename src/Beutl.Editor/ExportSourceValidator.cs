using Beutl.ProjectSystem;

namespace Beutl.Editor;

public static class ExportSourceValidator
{
    public static IReadOnlyList<string> GetMissingFileSources(IHierarchical root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return ProxySourceEnumerator.EnumerateMediaFileSources(root)
            .Where(static path => !File.Exists(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
