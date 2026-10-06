namespace Beutl.Controls;

internal static class DirectoryTreeRename
{
    public static bool HasDistinctDestination(string source, string destination)
    {
        if (!File.Exists(destination) && !Directory.Exists(destination))
            return false;

        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            return true;

        string? parent = Path.GetDirectoryName(destination);
        if (parent is null)
            return false;

        string targetName = Path.GetFileName(destination);
        return Directory.EnumerateFileSystemEntries(parent)
            .Any(entry => string.Equals(Path.GetFileName(entry), targetName, StringComparison.Ordinal));
    }
}
