using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext(
    Type ownerType,
    ISerializationErrorNotifier errorNotifier,
    ICoreSerializationContext? parent = null,
    JsonObject? json = null)
    : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = [];
    private List<(object, Guid, Action<ICoreSerializable>)>? _rootResolvers;
    private List<(Guid, Action<ICoreSerializable>)>? _resolvers;
    private Dictionary<Guid, ICoreSerializable>? _objects;
    private readonly JsonObject _json = json ?? [];

    public ICoreSerializationContext? Parent { get; } = parent;

    public JsonSerializationContext Root => IsRoot ? this : (Parent as JsonSerializationContext)!.Root;

    public CoreSerializationMode Mode => CoreSerializationMode.ReadWrite;

    public Type OwnerType { get; } = ownerType;

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => Parent == null;

    public ISerializationErrorNotifier ErrorNotifier { get; } = errorNotifier;

    public JsonObject GetJsonObject()
    {
        return _json;
    }

    public void SetJsonObject(JsonObject obj)
    {
        _json.Clear();
        JsonDeepClone.CopyTo(obj, _json);
    }

    public JsonNode? GetNode(string name)
    {
        return _json[name];
    }

    public void SetNode(string name, Type definedType, Type actualType, JsonNode? node)
    {
        _json[name] = node;
        _knownTypes[name] = (definedType, actualType);
    }

    public void Populate(string name, ICoreSerializable obj)
    {
        if (_json[name] is JsonObject jobj)
        {
            var context = new JsonSerializationContext(
                ownerType: obj.GetType(),
                errorNotifier: new RelaySerializationErrorNotifier(ErrorNotifier, name),
                parent: this,
                json: jobj);

            using (ThreadLocalSerializationContext.Enter(context))
            {
                obj.Deserialize(context);
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
                // Resolve references
                if (_rootResolvers == null || _objects == null)
                    return;

                for (int i = _rootResolvers.Count - 1; i >= 0; i--)
                {
                    var (self, id, callback) = _rootResolvers[i];
                    if (_objects.TryGetValue(id, out var resolved))
                    {
                        callback(resolved);
                        _rootResolvers.RemoveAt(i);
                    }
                    else
                    {
                        ObjectRegistry.Current.Resolve(id, self, (_, r) => callback(r));
                    }
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
        return _json.ContainsKey(name);
    }
}
