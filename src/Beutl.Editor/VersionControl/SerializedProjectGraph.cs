using Beutl.Serialization;

namespace Beutl.Editor.VersionControl;

// The set of files a project actually references, which is what tells required project state apart
// from whatever else happens to sit in the project directory.
internal static class SerializedProjectGraph
{
    public static IReadOnlySet<string> GetRelativePaths(string projectFile, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectFile));
        ExternalResourceCollector.SerializationGraph graph =
            ExternalResourceCollector.DiscoverSerializationGraph(project);
        foreach (Uri uri in graph.Objects
                     .Select(static obj => obj.Uri)
                     .Concat(graph.UnaddressableFileSources)
                     .Concat(graph.AddressableFileSources)
                     .OfType<Uri>())
        {
            if (!uri.IsFile)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(projectRoot, uri.LocalPath);
            if (!Path.IsPathFullyQualified(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                paths.Add(OperatingSystem.IsWindows()
                    ? relativePath.Replace('\\', '/')
                    : relativePath);
            }
        }

        return paths;
    }

    // The project file itself can be the conflicted one, and a half-written graph must not stop the
    // caller from scanning what it already knows about.
    public static IReadOnlySet<string> TryGetRelativePaths(string projectFile, string projectRoot)
    {
        try
        {
            return GetRelativePaths(projectFile, projectRoot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
