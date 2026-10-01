using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media;
using Beutl.NodeGraph;

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

        foreach (CoreObject obj in EnumerateObjects(root))
        {
            collector.CollectFromObject(obj, projectDirectory);
        }

        return collector;
    }

    // Node adapters own values and animations outside the normal hierarchy. Use
    // the same walk when resolving collected IDs during relocation.
    internal static IEnumerable<CoreObject> EnumerateObjects(IHierarchical root)
    {
        var visited = new HashSet<IHierarchical>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<IHierarchical>();
        pending.Push(root);
        while (pending.TryPop(out IHierarchical? item))
        {
            if (!visited.Add(item)) continue;
            if (item is CoreObject obj) yield return obj;
            foreach (IHierarchical child in item.HierarchicalChildren)
                pending.Push(child);
            if (item is INodeMember { Property: { } property })
            {
                if (property.GetValue() is IHierarchical value)
                    pending.Push(value);
                if (property is IAnimatablePropertyAdapter { Animation: { } animation })
                    pending.Push(animation);
            }
            if (item is IKeyFrame { Value: IHierarchical keyFrameValue })
                pending.Push(keyFrameValue);
        }
    }

    private void CollectFromObject(CoreObject obj, string projectDirectory)
    {
        if (obj is INodeMember { Property: { } adapter })
        {
            switch (adapter.GetValue())
            {
                case IFileSource source when RequiresRelocation(source, projectDirectory):
                    _fileSources.Add((obj.Id, nameof(INodeMember.Property), source.Uri));
                    break;
                case FontFamily font:
                    _fontFamilies.Add(font);
                    break;
            }
        }

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
