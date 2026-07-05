namespace Beutl.AgentToolkit.Common;

public static class PathComparison
{
    // Linux file systems are case-sensitive; Windows and the default macOS volume are not.
    public static StringComparison ForCurrentPlatform =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
