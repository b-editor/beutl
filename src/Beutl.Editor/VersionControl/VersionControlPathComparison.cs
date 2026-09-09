namespace Beutl.Editor.VersionControl;

// Keep repository callers on the same filesystem identity rules as the editor.
internal static class VersionControlPathComparison
{
    public static bool AreSameCanonicalPath(string left, string right)
        => FilePathComparison.AreSameCanonicalPath(left, right);

    public static bool IsSameOrDescendant(string root, string path)
        => FilePathComparison.IsSameOrDescendant(root, path);

    public static bool TryAreSameChildPath(
        string parentPath, string leftName, string rightName, out bool areSame)
        => FilePathComparison.TryAreSameChildPath(parentPath, leftName, rightName, out areSame);

    public static string ResolveCanonicalPath(string path)
        => FilePathComparison.ResolveCanonicalPath(path);

    internal static string SelectCanonicalExistingEntry(
        string component, string candidate, IEnumerable<string> entries)
        => FilePathComparison.SelectCanonicalExistingEntry(component, candidate, entries);
}
