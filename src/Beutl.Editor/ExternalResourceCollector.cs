using Beutl.Engine;
using Beutl.Graphics3D.Models;
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

        if (obj.Uri != null && IsExternalFile(obj.Uri, projectDirectory))
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
                    if (fileSource.Uri != null && RequiresRelocation(fileSource, projectDirectory))
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
                    if (RequiresRelocation(fileSource, projectDirectory))
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
    /// Determines whether a source or its model dependencies need to be relocated.
    /// </summary>
    internal static bool RequiresRelocation(IFileSource source, string projectDirectory)
        => IsExternalFile(source.Uri, projectDirectory)
           || source is ModelSource model
           && model.Dependencies.Any(path => IsExternalFile(new Uri(path), projectDirectory));

    private static bool IsExternalFile(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
            return false;

        string filePath = uri.LocalPath;
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        if (!fullProjectPath.EndsWith(Path.DirectorySeparatorChar))
            fullProjectPath += Path.DirectorySeparatorChar;

        // Different casing can name a distinct directory. Relocate even case aliases so
        // their relative references remain valid when imported on a case-sensitive volume.
        return !Path.GetFullPath(filePath).StartsWith(fullProjectPath, StringComparison.Ordinal);
    }
}
