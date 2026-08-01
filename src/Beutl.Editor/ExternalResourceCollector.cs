using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;

namespace Beutl.Editor;

/// <summary>
/// Collects IFileSource references and font references from the project hierarchy.
/// </summary>
public sealed class ExternalResourceCollector
{
    private readonly HashSet<(Guid Object, string PropertyName, Uri OriginalUri)> _fileSources = [];
    private readonly HashSet<FontFamily> _fontFamilies = [];

    private ExternalResourceCollector()
    {
    }

    /// <summary>
    /// The list of collected file sources.
    /// </summary>
    public IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> FileSources => _fileSources;

    /// <summary>
    /// The list of collected font families.
    /// </summary>
    public IEnumerable<FontFamily> FontFamilies => _fontFamilies;

    /// <summary>
    /// Collects all resource references within the hierarchy.
    /// </summary>
    /// <param name="root">The root hierarchy to start collecting from.</param>
    /// <param name="projectDirectory">The path of the project directory.</param>
    /// <returns>The collected resource information.</returns>
    public static ExternalResourceCollector Collect(IHierarchical root, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        ExternalResourceCollector collector = new();

        // Traverse all EngineObjects within the hierarchy
        foreach (CoreObject obj in root.EnumerateAllChildren<CoreObject>())
        {
            collector.CollectFromObject(obj, projectDirectory);
        }

        // Also process the root itself if it is a CoreObject
        if (root is CoreObject rootObj)
        {
            collector.CollectFromObject(rootObj, projectDirectory);
        }

        return collector;
    }

    private void CollectFromObject(CoreObject obj, string projectDirectory)
    {
        if (obj is EngineObject engineObj)
        {
            CollectFromEngineObject(engineObj, projectDirectory);
        }

        if (obj.Uri != null && ShouldRelocateFile(obj.Uri, projectDirectory))
        {
            _fileSources.Add((obj.Id, "Uri", obj.Uri));
        }

        var props = PropertyRegistry.GetRegistered(obj.GetType());
        foreach (var prop in props)
        {
            if (prop.PropertyType.IsValueType) continue;
            object? value = obj.GetValue(prop);
            switch (value)
            {
                case IFileSource fileSource:
                    if (fileSource.Uri != null && ShouldRelocateFile(fileSource.Uri, projectDirectory))
                    {
                        _fileSources.Add((obj.Id, prop.Name, fileSource.Uri));
                    }

                    break;
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    private void CollectFromEngineObject(EngineObject obj, string projectDirectory)
    {
        foreach (IProperty property in obj.Properties)
        {
            switch (property.CurrentValue)
            {
                // Collect IFileSource
                case IFileSource fileSource when fileSource.Uri != null:
                    if (ShouldRelocateFile(fileSource.Uri, projectDirectory))
                    {
                        _fileSources.Add((obj.Id, property.Name, fileSource.Uri));
                    }

                    break;
                // Collect FontFamily
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    /// <summary>
    /// Determines whether the URI must be copied into the package's resources directory.
    /// </summary>
    private static bool ShouldRelocateFile(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
            return false;

        string filePath = Path.GetFullPath(uri.LocalPath);
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        string relativePath = Path.GetRelativePath(fullProjectPath, filePath);

        // Files outside the project directory are considered external.
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return true;
        }

        // Directory staging deliberately skips links. A referenced file that is itself a link,
        // or lives below a linked directory, must therefore go through the regular relocation
        // path so only that referenced target is materialized in resources. Inspect each lexical
        // component without resolving targets, which also identifies broken links and cycles.
        string currentPath = fullProjectPath;
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath = Path.Combine(currentPath, segments[i]);
            FileSystemInfo info = i == segments.Length - 1
                ? new FileInfo(currentPath)
                : new DirectoryInfo(currentPath);

            try
            {
                if (info.LinkTarget is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException)
            {
                // Conservatively relocate when link inspection is unavailable. The relocation
                // service will either copy the referenced file or report it as a partial failure.
                return true;
            }
        }

        return false;
    }
}
