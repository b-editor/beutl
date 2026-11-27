using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal static class AgentMarkdownImages
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static IReadOnlyList<AgentImagePreview> GetPreviews(string text, string? projectDirectory)
    {
        if (!text.Contains("![", StringComparison.Ordinal))
            return [];

        var images = new List<AgentImagePreview>();
        foreach (var image in Markdown.Parse(text, s_pipeline).Descendants<LinkInline>())
        {
            if (!image.IsImage || ResolvePath(image.Url, projectDirectory) is not { } path)
                continue;

            string alt = string.Concat(image.Descendants<LiteralInline>().Select(inline => inline.Content.ToString())).Trim();
            if (AgentImagePreview.FromFile(path, alt.Length > 0 ? alt : Path.GetFileName(path), IsLocalFile) is { } preview)
                images.Add(preview);
        }
        return images;
    }

    private static string? ResolvePath(string? destination, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return null;

        try
        {
            string path;
            if (destination.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || !uri.IsFile || uri.IsUnc
                    || uri.Host.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                    || !Path.IsPathFullyQualified(uri.LocalPath))
                    return null;
                path = uri.LocalPath;
            }
            else
            {
                path = Uri.UnescapeDataString(destination);
            }

            if (HasNetworkPrefix(path))
                return null;

            if (Path.IsPathFullyQualified(path))
                return ResolveLocalPath(Path.GetFullPath(path));

            if (projectDirectory is null || Path.IsPathRooted(path) || path.Contains(':'))
                return null;

            string fullPath = Path.GetFullPath(path, projectDirectory);
            return IsWithinDirectory(projectDirectory, fullPath)
                && ResolveLocalPath(projectDirectory) is { } localDirectory
                && ResolveLocalPath(fullPath) is { } localFile
                && IsWithinDirectory(localDirectory, localFile) ? localFile : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasNetworkPrefix(string path)
        => path.StartsWith('\\') || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("/\\", StringComparison.Ordinal);

    private static bool IsWithinDirectory(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string? ResolveLocalPath(string path, int linksRemaining = 8)
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
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (OperatingSystem.IsWindows() || linksRemaining == 0)
                    return null;
                var target = attributes.HasFlag(FileAttributes.Directory)
                    ? Directory.ResolveLinkTarget(component, returnFinalTarget: false)
                    : File.ResolveLinkTarget(component, returnFinalTarget: false);
                if (target is null)
                    return null;
                string redirected = end < 0 ? target.FullName : Path.Combine(target.FullName, path[(end + 1)..]);
                return ResolveLocalPath(redirected, linksRemaining - 1);
            }
            if (end < 0)
                return path;
        }
    }

    private static bool IsLocalFile(string path)
    {
        try
        {
            // Recheck the resolved path when a thumbnail or lightbox opens it.
            return ResolveLocalPath(path, linksRemaining: 0) is not null
                && !File.GetAttributes(path).HasFlag(FileAttributes.Directory)
                && new FileInfo(path).Length is > 0 and <= AgentAttachment.MaxImageBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
