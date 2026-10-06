using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.Editor;

/// <summary>Discovers serialized objects and file references in a project hierarchy.</summary>
internal static partial class VersionControlSerializationGraph
{
    internal static SerializationGraph DiscoverSerializationGraph(IHierarchical root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var visitor = new SerializationGraphVisitor();
        visitor.Visit(root);
        return new SerializationGraph(
            visitor.Objects,
            visitor.UnaddressableFileSources,
            visitor.AddressableFileSources);
    }

    internal static bool IsInReservedProjectPath(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
        {
            return false;
        }

        string filePath = Path.GetFullPath(uri.LocalPath);
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        string relativePath = Path.GetRelativePath(fullProjectPath, filePath);
        if (Path.IsPathFullyQualified(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsReservedPath(relativePath);
    }

    private static bool ContainsReservedPath(string relativePath)
    {
        // This value comes from Path.GetRelativePath. A backslash is an ordinary filename
        // character on Unix, while both slash forms are separators on Windows.
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(static segment =>
        {
            string portableName = segment;
            if (OperatingSystem.IsWindows())
            {
                int streamSeparator = segment.IndexOf(':');
                portableName = (streamSeparator >= 0 ? segment[..streamSeparator] : segment)
                    .TrimEnd(' ', '.');
            }

            return string.Equals(portableName, ".git", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(portableName, ".beutl", StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class SerializationGraphContext(
        SerializationGraphVisitor visitor,
        ICoreSerializable owner) : IJsonSerializationContext
    {
        private readonly JsonObject _json = [];
        private readonly Dictionary<string, (Type DefinedType, Type ActualType)>
            _pendingNodes = [];
        private readonly Dictionary<string, object?> _values = [];

        public CoreSerializationMode Mode
            => CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects;

        public Uri? BaseUri => (owner as CoreObject)?.Uri;

        public Type OwnerType => owner.GetType();

        public void ReportPersistedContentMigration(string minAppVersion)
        {
            // Round-trip inspection deserializes temporary values to discover their resources.
            // A valid migration report belongs to that temporary graph and must not mutate or
            // reject the live project being inspected.
        }

        public JsonObject GetJsonObject()
        {
            throw new InvalidDataException(
                "Cannot safely expose mutable serialized JSON during resource inspection.");
        }

        public void SetJsonObject(JsonObject obj)
        {
            throw new InvalidDataException(
                "Cannot safely inspect raw serialized JSON for external resources.");
        }

        public JsonNode? GetNode(string name)
        {
            throw new InvalidDataException(
                "Cannot safely expose mutable serialized JSON during resource inspection.");
        }

        public void SetNode(string name, Type definedType, Type actualType, JsonNode? node)
        {
            _values.Remove(name);
            _json[name] = node;
            _pendingNodes[name] = (definedType, actualType);
        }

        public void SetValue<T>(string name, T? value)
        {
            if (value is System.Reactive.Unit)
            {
                _values.Remove(name);
                _pendingNodes.Remove(name);
                _json.Remove(name);
                return;
            }

            visitor.VisitSerializedValue(owner, name, value);
            _pendingNodes.Remove(name);
            _json.Remove(name);
            _values[name] = value;
        }

        public T? GetValue<T>(string name)
        {
            if (_values.TryGetValue(name, out object? value))
            {
                if (value is null)
                {
                    return default;
                }

                if (value is T typed)
                {
                    return typed;
                }

                throw new InvalidDataException(
                    $"Cannot reproduce typed serialization read for '{name}'.");
            }

            if (_pendingNodes.ContainsKey(name))
            {
                throw new InvalidDataException(
                    $"Cannot reproduce serialized node read for '{name}'.");
            }

            return default;
        }

        public bool Contains(string name)
        {
            return _values.ContainsKey(name) || _pendingNodes.ContainsKey(name);
        }

        public void Populate(string name, ICoreSerializable obj)
        {
            visitor.Visit(obj);
        }

        public void Resolve(Guid id, Action<ICoreSerializable> callback)
        {
        }

        public void Complete()
        {
            if (_json.Count != _pendingNodes.Count
                || _json.Any(item => !_pendingNodes.ContainsKey(item.Key)))
            {
                throw new InvalidDataException(
                    "Serialized JSON was mutated outside a typed serialization contract.");
            }

            foreach ((string name, (Type definedType, Type actualType)) in _pendingNodes)
            {
                visitor.VisitSerializedNodeValue(
                    owner,
                    name,
                    definedType,
                    actualType,
                    _json[name]);
            }
        }
    }

    internal sealed record SerializationGraph(
        IReadOnlyList<CoreObject> Objects,
        IReadOnlySet<Uri> UnaddressableFileSources,
        IReadOnlySet<Uri> AddressableFileSources);
}
