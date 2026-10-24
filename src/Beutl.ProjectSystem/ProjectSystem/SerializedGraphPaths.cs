using System.Collections;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal readonly record struct SerializedGraphPath(string Stable, string Positional);

// Paths of the CoreObjects in an element's serialized graph. The stable path names a collection item by
// its Id where it has one; the positional path only uses indices.
internal static class SerializedGraphPaths
{
    public static IEnumerable<(CoreObject Object, SerializedGraphPath Path)> EnumerateSerializedGraphDescendantPaths(
        Element element)
    {
        var mapping = new PathMapping(recovery: true);
        CoreSerializer.Visit(element, mapping.Add);
        return mapping.Enumerate(element).Select(item => ((CoreObject)item.Object, item.Path));
    }

    internal static IEnumerable<(object Object, SerializedGraphPath Path)> EnumerateTraversal(object? root)
    {
        if (root is IOptional optional) root = optional.ToObject().GetValueOrDefault();
        if (root is null) return [];
        var mapping = new PathMapping(recovery: false);
        CoreSerializer.Visit(root, mapping.Add);
        var descendants = mapping.Enumerate(root);
        return root is CoreObject or IFallback
            ? new[] { (root, new SerializedGraphPath("$", "$")) }.Concat(descendants)
            : descendants;
    }

    // Only serializer visits supply edges. Hierarchy indices and property order translate those edges to released keys.
    private sealed class PathMapping(bool recovery)
    {
        private readonly Dictionary<object, List<Edge>> _edges = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, CollectionIndex> _collections = new(ReferenceEqualityComparer.Instance);

        public bool Add(CoreSerializationVisit visit)
        {
            if (visit.Value is not (CoreObject or IFallback) || recovery && visit.Value is not CoreObject
                || visit.Parent is null) return false;
            object child = visit.Value;
            var segments = new List<CoreSerializationVisit>();
            CoreSerializationVisit? owner = visit.Parent;
            segments.Add(visit);
            while (owner?.Parent is not null && owner.Value is not CoreObject && (recovery || owner.Value is not IFallback))
            {
                segments.Add(owner);
                owner = owner.Parent;
            }

            if (owner is null) return false;
            object parent = owner.Value;
            segments.Reverse();
            string? member = segments[0].MemberName;
            IProperty? property = (parent as CoreObject)?.Properties.FirstOrDefault(p => p.Name == member);
            var suffix = new SerializedGraphPath("", "");
            int group;
            int order;
            int part = 0;

            if (child is IKeyFrame && parent is IKeyFrameAnimation
                && owner.DictionaryKey is { } animationName
                && owner.Parent is { MemberName: "Animations", Parent.Value: EngineObject engine })
            {
                parent = engine;
                property = engine.Properties.First(p => p.Name == animationName);
                suffix = recovery
                    ? Item(Append(suffix, "animation", animationName), visit.Parent!.Value, visit.Index!.Value)
                    : Plain($"/Animations/{animationName}/KeyFrames[{visit.Index}]");
                group = 0;
                order = PropertyIndex(engine, property);
                part = 1;
            }
            else if (parent is Element && member == nameof(Element.Objects)
                     || !recovery && parent is Scene && member == "Elements"
                     || parent is EngineObject engineOwner && property is not null && !engineOwner.IsBaseField(property))
            {
                suffix = Members(suffix, segments);
                group = 0;
                order = property is null ? 0 : PropertyIndex((CoreObject)parent, property);
            }
            else if (!recovery && parent is CoreObject coreOwner && property is not null
                     && coreOwner.IsBaseField(property) && CoreObject.IsAutoSerialized(property))
            {
                suffix = Members(suffix, segments);
                group = 1;
                order = PropertyIndex(coreOwner, property);
            }
            else if (!recovery && member == "Setter" && segments.Count == 2
                     && segments[1].MemberName is "Value" or "Animation")
            {
                suffix = Plain("/Setter");
                group = 2;
                order = segments[1].MemberName == "Value" ? 0 : 1;
            }
            else if (parent is IHierarchical hierarchical
                     && Index(hierarchical.HierarchicalChildren).Positions.TryGetValue(child, out int position))
            {
                suffix = recovery
                    ? Item(Append(suffix, "collection", "HierarchicalChildren"), hierarchical.HierarchicalChildren, position)
                    : Plain($"/HierarchicalChildren[{position}]");
                group = recovery ? 1 : 3;
                order = position;
            }
            else
            {
                suffix = Members(suffix, segments);
                group = recovery ? 2 : 4;
                order = property is null ? int.MaxValue : PropertyIndex((CoreObject)parent, property);
            }

            if (!_edges.TryGetValue(parent, out List<Edge>? edges)) _edges[parent] = edges = [];
            edges.Add(new Edge(child, suffix, group, order, part));
            return false;
        }

        public IEnumerable<(object Object, SerializedGraphPath Path)> Enumerate(object root)
        {
            var result = new List<(object, SerializedGraphPath)>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
            AppendChildren(root, new SerializedGraphPath("$", "$"));
            return result;

            void AppendChildren(object parent, SerializedGraphPath path)
            {
                if (!_edges.TryGetValue(parent, out List<Edge>? edges)) return;
                foreach (Edge edge in edges.OrderBy(e => e.Group).ThenBy(e => e.Order).ThenBy(e => e.Part))
                {
                    if (!visited.Add(edge.Child)) continue;
                    var childPath = new SerializedGraphPath(path.Stable + edge.Suffix.Stable, path.Positional + edge.Suffix.Positional);
                    result.Add((edge.Child, childPath));
                    AppendChildren(edge.Child, childPath);
                }
            }
        }

        private SerializedGraphPath Members(SerializedGraphPath path, List<CoreSerializationVisit> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                CoreSerializationVisit segment = segments[i];
                if (segment.Index is { } entryIndex && segment.Parent!.Value is IDictionary dictionary
                    && i + 1 < segments.Count && segments[i + 1].MemberName == "Value")
                {
                    path = recovery
                        ? Append(path, "key", Index(dictionary).DictionaryKeys[entryIndex])
                        : Plain($"{path.Positional}[{entryIndex}]");
                    i++;
                }
                else if (!recovery)
                {
                    string part = segment.Index is { } itemIndex ? $"[{itemIndex}]"
                        : segment.DictionaryKey is { } key ? $"[{Index(segment.Parent!.Value).Keys[key]}]"
                        : $"/{segment.MemberName}";
                    path = Plain(path.Positional + part);
                }
                else if (segment.Index is { } index)
                    path = Item(path, segment.Parent!.Value, index);
                else
                    path = Append(path, segment.DictionaryKey is null ? "property" : "key",
                        segment.MemberName ?? segment.DictionaryKey!);
            }

            return path;
        }

        private SerializedGraphPath Item(SerializedGraphPath path, object collection, int index)
        {
            string indexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string positional = AppendSerializedGraphPath(path.Positional, "index", indexText);
            object? original = Index(collection).Values[index];
            string stable = original is CoreObject { Id.Value: var id } && id != Guid.Empty
                ? AppendSerializedGraphPath(path.Stable, "id", $"{id:D}#{Index(collection).Occurrences[index]}")
                : AppendSerializedGraphPath(path.Stable, "index", indexText);
            return new SerializedGraphPath(stable, positional);
        }

        private CollectionIndex Index(object collection)
        {
            if (_collections.TryGetValue(collection, out CollectionIndex? result)) return result;
            result = new CollectionIndex();
            var counts = new Dictionary<Guid, int>();
            IEnumerable items = collection is IDictionary dictionary
                ? dictionary.Keys.Cast<object>().Select(key => new DictionaryEntry(key, dictionary[key]))
                : (IEnumerable)collection;
            foreach (object? entry in items)
            {
                object? item = entry;
                if (entry is DictionaryEntry pair)
                {
                    string key = pair.Key?.ToString() ?? "null";
                    result.Keys[key] = result.Occurrences.Count;
                    result.DictionaryKeys.Add(key);
                    item = pair.Value;
                }
                int occurrence = 0;
                if (item is CoreObject obj)
                {
                    result.Positions.TryAdd(obj, result.Occurrences.Count);
                    counts.TryGetValue(obj.Id.Value, out occurrence);
                    counts[obj.Id.Value] = occurrence + 1;
                }
                result.Occurrences.Add(occurrence);
                result.Values.Add(item);
            }

            _collections.Add(collection, result);
            return result;
        }

        private static int PropertyIndex(CoreObject owner, IProperty property)
            => owner.Properties.TakeWhile(p => !ReferenceEquals(p, property)).Count();

        private sealed record Edge(object Child, SerializedGraphPath Suffix, int Group, int Order, int Part);

        private sealed class CollectionIndex
        {
            public Dictionary<object, int> Positions { get; } = new(ReferenceEqualityComparer.Instance);
            public List<int> Occurrences { get; } = [];
            public List<object?> Values { get; } = [];
            public Dictionary<string, int> Keys { get; } = new(StringComparer.Ordinal);
            public List<string> DictionaryKeys { get; } = [];
        }
    }

    private static SerializedGraphPath Plain(string path) => new(path, path);

    private static SerializedGraphPath Append(SerializedGraphPath path, string kind, string value)
        => new(AppendSerializedGraphPath(path.Stable, kind, value), AppendSerializedGraphPath(path.Positional, kind, value));

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
