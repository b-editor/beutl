namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class PathScope
{
    public static bool IsUnderDirectory(string path, string directory)
    {
        string normalizedDir = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);

        return FilePathComparison.StartsWith(normalizedPath, normalizedDir)
            || FilePathComparison.Equals(normalizedPath + Path.DirectorySeparatorChar, normalizedDir);
    }
}
