using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab;

internal sealed record AgentMarkdownLocalPath(string FullPath, bool IsDirectory)
{
    internal static string? Decode(string? destination, bool allowAbsolutePath = false)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return null;

        if (destination.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || !uri.IsFile || uri.IsUnc
                || uri.Host.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || !Path.IsPathFullyQualified(uri.LocalPath))
                return null;
            return HasNetworkPrefix(uri.LocalPath) ? null : uri.LocalPath;
        }

        if (!allowAbsolutePath && destination.AsSpan().ContainsAny('?', '#'))
            return null;

        string path = Uri.UnescapeDataString(destination);
        if (path.Length == 0 || path.Contains('\0') || HasNetworkPrefix(path))
            return null;
        if (Path.IsPathFullyQualified(path))
            return allowAbsolutePath ? path : null;
        return Path.IsPathRooted(path) || path.Contains(':') ? null : path;
    }

    internal static AgentMarkdownLocalPath? Resolve(string path, string? projectDirectory)
    {
        try
        {
            if (Path.IsPathFullyQualified(path))
                return ResolveLocalPath(Path.GetFullPath(path));
            if (projectDirectory is null)
                return null;

            string fullPath = Path.GetFullPath(path, projectDirectory);
            return IsWithinDirectory(projectDirectory, fullPath)
                && ResolveLocalPath(projectDirectory) is { IsDirectory: true } localDirectory
                && ResolveLocalPath(fullPath) is { } localTarget
                && IsWithinDirectory(localDirectory.FullPath, localTarget.FullPath) ? localTarget : null;
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            return null;
        }
    }

    internal static bool IsLocalImageFile(string path)
    {
        try
        {
            // Recheck the resolved path when a thumbnail or lightbox opens it.
            return ResolveLocalPath(path, linksRemaining: 0) is { IsDirectory: false }
                && new FileInfo(path).Length is > 0 and <= AgentAttachment.MaxImageBytes;
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            return false;
        }
    }

    private static bool IsPathException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static bool HasNetworkPrefix(string path)
        => path.StartsWith('\\') || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("/\\", StringComparison.Ordinal);

    private static bool IsWithinDirectory(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static AgentMarkdownLocalPath? ResolveLocalPath(string path, int linksRemaining = 8)
    {
        if (HasNetworkPrefix(path) || Path.GetPathRoot(path) is not { } root)
            return null;
        if (OperatingSystem.IsWindows()
            && (root.Length != 3 || root[1] != ':' || path.AsSpan(2).Contains(':')
                || new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.CDRom)))
            return null;

        // Check parents before touching children. Windows reparse points can redirect to UNC shares.
        for (int end = path.IndexOf(Path.DirectorySeparatorChar, root.Length); ; end = path.IndexOf(Path.DirectorySeparatorChar, end + 1))
        {
            string component = end < 0 ? path : path[..end];
            var attributes = File.GetAttributes(component);
            bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (OperatingSystem.IsWindows() || linksRemaining == 0)
                    return null;
                var target = isDirectory
                    ? Directory.ResolveLinkTarget(component, returnFinalTarget: false)
                    : File.ResolveLinkTarget(component, returnFinalTarget: false);
                if (target is null)
                    return null;
                string redirected = end < 0 ? target.FullName : Path.Combine(target.FullName, path[(end + 1)..]);
                return ResolveLocalPath(redirected, linksRemaining - 1);
            }
            if (end < 0)
                return new(path, isDirectory);
        }
    }
}
