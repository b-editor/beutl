namespace Beutl.Editor;

public static class FileSystemPathComparison
{
    // Windows and the Apple platforms are case-insensitive; every other Unix is case-sensitive.
    // This is the same split System.IO.PathInternal.IsCaseSensitive makes, so anything deciding
    // whether two spellings name the same directory must match it or it contradicts
    // Path.GetRelativePath. Note it is a per-platform default, not a per-volume fact: a
    // case-sensitive APFS volume compares case-insensitively here, exactly as System.IO does.
    public static bool IsCaseInsensitive
        => OperatingSystem.IsWindows()
           || OperatingSystem.IsMacOS()
           || OperatingSystem.IsMacCatalyst()
           || OperatingSystem.IsIOS()
           || OperatingSystem.IsTvOS();

    public static StringComparison ForCurrentPlatform
        => IsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer ComparerForCurrentPlatform
        => IsCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
