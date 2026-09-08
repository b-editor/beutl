using System.Runtime.InteropServices;

namespace Beutl;

/// <summary>
/// How to compare two file paths for identity on the current platform.
/// </summary>
/// <remarks>
/// Windows and macOS resolve paths case-insensitively; Linux does not, where <c>Item.json</c> and
/// <c>item.json</c> are two different files. Comparing case-insensitively everywhere makes distinct
/// files collide, so anything keying a cache or a containment test on a path uses this.
/// </remarks>
public static class FilePathComparison
{
    public static StringComparison Comparison { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    public static bool Equals(string? left, string? right) => string.Equals(left, right, Comparison);

    public static bool StartsWith(string path, string prefix) => path.StartsWith(prefix, Comparison);
}
