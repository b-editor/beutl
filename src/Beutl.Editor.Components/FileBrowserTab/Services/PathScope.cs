namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class PathScope
{
    public static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            return FilePathComparison.IsSameOrDescendant(directory, path);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }
}
