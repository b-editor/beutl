using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class PathScope
{
    // Windows and macOS resolve paths case-insensitively; Linux does not, where '.beutl/Templates'
    // and '.beutl/templates' are two different directories. Comparing case-insensitively everywhere
    // would let an unrelated directory be read as the configured one.
    private static readonly StringComparison s_comparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    public static bool IsUnderDirectory(string path, string directory)
    {
        string normalizedDir = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);

        return normalizedPath.StartsWith(normalizedDir, s_comparison)
            || string.Equals(normalizedPath + Path.DirectorySeparatorChar, normalizedDir, s_comparison);
    }
}
