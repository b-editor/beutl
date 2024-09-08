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
            }
        }
    }

    public void AfterDeserialized(ICoreSerializable obj)
    {
        if (obj is CoreObject coreObject)
        {
            SetObjectAndId(coreObject);

            if (IsRoot)
            {
                // Resolve references
                if (_resolvers == null)
                    return;

                for (int i = _resolvers.Count - 1; i >= 0; i--)
                {
                    var (id, callback) = _resolvers[i];
                    if (_objects.TryGetValue(id, out var resolved))
                    {
                        callback(resolved);
                    }

                    _resolvers.RemoveAt(i);
                }

                // TODO: アプリケーション全体から解決できるようになれば、
                // ここにそのコードを追加する。
            }
        }
    }

    [MemberNotNull(nameof(_objects))]
    private void SetObjectAndId(CoreObject coreObject)
    {
        if (IsRoot)
        {
            _objects ??= new();
            _objects[coreObject.Id] = coreObject;
        }
        else
        {
            Root.SetObjectAndId(coreObject);
        }
    }

    public void Resolve(Guid id, Action<ICoreSerializable> callback)
    {
        if (IsRoot)
        {
            _resolvers ??= new();
            _resolvers.Add((id, callback));
        }
        else
        {
            Parent.Resolve(id, callback);
        }
    }

    public bool Contains(string name)
    {
        return _json.ContainsKey(name);
    }
}
