using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext(
    Type ownerType,
    ICoreSerializationContext? parent = null,
    JsonObject? json = null,
    CoreSerializerOptions? options = null)
    : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = [];
    private List<(object, Guid, Action<ICoreSerializable>)>? _rootResolvers;
    private List<(Guid, Action<ICoreSerializable>)>? _resolvers;
    private Dictionary<Guid, ICoreSerializable>? _objects;
    private readonly JsonObject _json = json ?? [];
    private Dictionary<string, CapturedValue>? _preservedValues;

    internal sealed record CapturedValue(object? Value, CoreObject[] Objects);

    internal Dictionary<string, CapturedValue>? CapturedValues { get; init; }

    internal void CaptureValue(string name, object? value, SerializedObjectCapture? objects = null)
    {
        if (CapturedValues is not null)
        {
            CapturedValues[name] = new CapturedValue(value, objects?.Objects.ToArray() ?? []);
        }
    }

    internal void PreserveUnchangedValues(JsonObject current, Dictionary<string, CapturedValue> values)
    {
        foreach ((string name, CapturedValue captured) in values)
        {
            if (captured.Value is not null
                && _json.TryGetPropertyValue(name, out JsonNode? desired)
                && desired is JsonObject or JsonArray
                && current.TryGetPropertyValue(name, out JsonNode? existing)
                && JsonNode.DeepEquals(existing, desired))
            {
                (_preservedValues ??= [])[name] = captured;
            }
        }
    }

    internal bool TryGetPreservedValue(string name, Type type, out object? value, bool allowCollections = false)
    {
        MarkRead(name);
        // Property readers can skip assigning an unchanged collection. Ordinary GetValue calls
        // must not return it: custom readers may clear and refill their destination collection.
        if (_preservedValues is not null && _preservedValues.TryGetValue(name, out CapturedValue? captured)
            && (allowCollections || captured.Value is not IEnumerable))
        {
            value = captured.Value;
            if (type.IsInstanceOfType(value))
            {
                RegisterPreservedObjects(captured);
                return true;
            }

            // Some property readers write T but read Optional<T> to distinguish omission from null.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>)
                && type.GenericTypeArguments[0].IsInstanceOfType(value))
            {
                value = Activator.CreateInstance(type, [value]);
                RegisterPreservedObjects(captured);
                return true;
            }
        }

        value = null;
        return false;
    }

    private void RegisterPreservedObjects(CapturedValue captured)
    {
        // Only register a member once its reader actually reuses it. Indexing the whole snapshot
        // would let references resolve to objects that the desired document removes or replaces.
        if (captured.Value is CoreObject coreObject)
        {
            SetObjectAndId(coreObject);
        }

        foreach (CoreObject obj in captured.Objects)
        {
            SetObjectAndId(obj);
        }
    }

    public ICoreSerializationContext? Parent { get; } = parent;

    public JsonSerializationContext Root => IsRoot ? this : (Parent as JsonSerializationContext)!.Root;

    public CoreSerializationMode Mode => options?.Mode ?? Parent?.Mode ?? CoreSerializationMode.ReadWrite;

    public Uri? BaseUri => options?.BaseUri ?? Parent?.BaseUri;

    public Type OwnerType { get; } = ownerType;

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => Parent == null;

    public JsonObject GetJsonObject()
    {
        // The caller can read any key, so none of them is an unknown member.
        _readsAllKeys = true;
        return _json;
    }

    public void SetJsonObject(JsonObject obj)
    {
        CapturedValues?.Clear();
        _json.Clear();
        JsonDeepClone.CopyTo(obj, _json);
    }

    public JsonNode? GetNode(string name)
    {
        MarkRead(name);
        return _json[name];
    }

    public void SetNode(string name, Type definedType, Type actualType, JsonNode? node)
    {
        CapturedValues?.Remove(name);
        _json[name] = node;
        _knownTypes[name] = (definedType, actualType);
    }

    public void Populate(string name, ICoreSerializable obj)
    {
        MarkRead(name);
        if (_json[name] is JsonObject jobj)
        {
            var context = new JsonSerializationContext(
                ownerType: obj.GetType(),
                parent: this,
                json: jobj);

            using (ThreadLocalSerializationContext.Enter(context))
            {
                obj.Deserialize(context);
                context.KeepUnknownMembers(obj);
                context.AfterDeserialized(obj);
            }
        }
    }

    public void AfterDeserialized(ICoreSerializable obj)
    {
        if (_resolvers?.Count > 0)
        {
            Root._rootResolvers ??= [];
            Root._rootResolvers.AddRange(_resolvers.Select(t => ((object)obj, t.Item1, t.Item2)));
            _resolvers.Clear();
        }

        if (obj is CoreObject coreObject)
        {
            SetObjectAndId(coreObject);

            if (IsRoot)
            {
                ResolvePendingRootReferences(coreObject);
            }
        }
    }

    // Resolve references
    private void ResolvePendingRootReferences(CoreObject coreObject)
    {
        if (_rootResolvers is not null && _objects is not null)
        {
            for (int i = _rootResolvers.Count - 1; i >= 0; i--)
            {
                var item = _rootResolvers[i];
                var (self, id, callback) = item;
                if (_objects.TryGetValue(id, out var resolved))
                {
                    callback(resolved);
                    _rootResolvers.RemoveAt(i);
                }
                else if (coreObject is IHierarchical hierarchical)
                {
                    var resolver = new ReferenceResolver(hierarchical, id);
                    resolver.Resolve().ContinueWith(t =>
                    {
                        callback(t.Result);
                        _rootResolvers?.Remove(item);
                    });
                }
                else
                {
                    // Error
                }
            }
        }
    }

    private void SetObjectAndId(CoreObject coreObject)
    {
        if (IsRoot)
        {
            _objects ??= new Dictionary<Guid, ICoreSerializable>();
            _objects[coreObject.Id] = coreObject;
        }
        else
        {
            Root.SetObjectAndId(coreObject);
        }
    }

    public void Resolve(Guid id, Action<ICoreSerializable> callback)
    {
        _resolvers ??= [];
        _resolvers.Add((id, callback));
    }

    public bool Contains(string name)
    {
        // A type that asks for a key knows it, even when it then leaves the value alone.
        MarkRead(name);
        return _json.ContainsKey(name);
    }
}
