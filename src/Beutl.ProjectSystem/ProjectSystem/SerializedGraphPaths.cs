using System.Collections;
using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.ProjectSystem;

internal readonly record struct SerializedGraphPath(string Stable, string Positional);

// Paths of the CoreObjects in an element's serialized graph. The stable path names a collection item by
// its Id where it has one; the positional path only uses indices.
internal static class SerializedGraphPaths
{
    public static IEnumerable<(CoreObject Object, SerializedGraphPath Path)> EnumerateSerializedGraphDescendantPaths(
        Element element)
    {
        var objects = new List<(CoreObject Object, SerializedGraphPath Path)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectSerializedGraphObjectPaths(element, new SerializedGraphPath("$", "$"), visited, objects);
        return objects.Where(item => !ReferenceEquals(item.Object, element));
    }

    private static void CollectSerializedGraphObjectPaths(
        object? value,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is IOptional optional)
        {
            if (optional.HasValue)
            {
                CollectSerializedGraphObjectPaths(optional.ToObject().Value, path, visited, objects);
            }

            return;
        }

        if (value is CoreObject coreObject)
        {
            objects.Add((coreObject, path));
        }

        if (value is Element element)
        {
            CollectSerializedGraphPathItems(
                element.Objects,
                AppendSerializedGraphPath(path, "property", nameof(Element.Objects)),
                visited,
                objects);
        }

        if (value is EngineObject engineObject)
        {
            CollectEngineObjectPaths(engineObject, path, visited, objects);
        }

        if (value is IHierarchical hierarchical)
        {
            CollectSerializedGraphPathItems(
                hierarchical.HierarchicalChildren,
                AppendSerializedGraphPath(path, "collection", "HierarchicalChildren"),
                visited,
                objects);
        }

        if (value is CoreObject registeredObject)
        {
            CollectRegisteredPropertyPaths(registeredObject, path, visited, objects);
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                CollectSerializedGraphObjectPaths(
                    entry.Value,
                    AppendSerializedGraphPath(path, "key", entry.Key?.ToString() ?? "null"),
                    visited,
                    objects);
            }
        }
        else if (value is IEnumerable enumerable)
        {
            CollectSerializedGraphPathItems(enumerable, path, visited, objects);
        }
    }

    private static void CollectEngineObjectPaths(
        EngineObject engineObject,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            SerializedGraphPath propertyPath = AppendSerializedGraphPath(path, "property", property.Name);
            CollectSerializedGraphObjectPaths(property.CurrentValue, propertyPath, visited, objects);
            if (property.Animation is IKeyFrameAnimation animation)
            {
                SerializedGraphPath keyFramesPath = AppendSerializedGraphPath(
                    path,
                    "animation",
                    property.Name);
                var occurrences = new Dictionary<Guid, int>();
                int index = 0;
                foreach (IKeyFrame keyFrame in animation.KeyFrames)
                {
                    SerializedGraphPath keyFramePath = CreateSerializedGraphCollectionItemPath(
                        keyFramesPath,
                        keyFrame,
                        index++,
                        occurrences);
                    CollectSerializedGraphObjectPaths(keyFrame, keyFramePath, visited, objects);
                    CollectSerializedGraphObjectPaths(
                        keyFrame.Value,
                        AppendSerializedGraphPath(keyFramePath, "property", nameof(IKeyFrame.Value)),
                        visited,
                        objects);
                }
            }
        }
    }

    private static void CollectRegisteredPropertyPaths(
        CoreObject registeredObject,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(registeredObject.GetType()))
        {
            if (!property.GetMetadata<CorePropertyMetadata>(registeredObject.GetType()).ShouldSerialize)
            {
                continue;
            }

            CollectSerializedGraphObjectPaths(
                registeredObject.GetValue(property),
                AppendSerializedGraphPath(path, "property", property.Name),
                visited,
                objects);
        }
    }

    private static void CollectSerializedGraphPathItems(
        IEnumerable items,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        var occurrences = new Dictionary<Guid, int>();
        int index = 0;
        foreach (object? item in items)
        {
            SerializedGraphPath itemPath = CreateSerializedGraphCollectionItemPath(
                path,
                item,
                index++,
                occurrences);
            CollectSerializedGraphObjectPaths(item, itemPath, visited, objects);
        }
    }

    private static SerializedGraphPath CreateSerializedGraphCollectionItemPath(
        SerializedGraphPath path,
        object? item,
        int index,
        IDictionary<Guid, int> occurrences)
    {
        string indexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string positional = AppendSerializedGraphPath(path.Positional, "index", indexText);
        if (item is CoreObject { Id: var id } && id != Guid.Empty)
        {
            int occurrence = occurrences.TryGetValue(id, out int value) ? value : 0;
            occurrences[id] = occurrence + 1;
            string stable = AppendSerializedGraphPath(path.Stable, "id", $"{id:D}#{occurrence}");
            return new SerializedGraphPath(stable, positional);
        }

        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, "index", indexText),
            positional);
    }

    private static SerializedGraphPath AppendSerializedGraphPath(
        SerializedGraphPath path,
        string kind,
        string value)
    {
        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, kind, value),
            AppendSerializedGraphPath(path.Positional, kind, value));
    }

    public static string NormalizeSerializedGraphPositionalPath(string path)
    {
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith("index:", StringComparison.Ordinal))
            {
                segments[i] = "index:*";
            }
        }

        return string.Join('/', segments);
    }

    private static string AppendSerializedGraphPath(string path, string kind, string value)
    {
        string escaped = value.Replace("~", "~0").Replace("/", "~1");
        return $"{path}/{kind}:{escaped}";
    }
}
