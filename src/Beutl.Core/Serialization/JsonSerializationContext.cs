using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext(
    Type ownerType, ISerializationErrorNotifier errorNotifier,
    ICoreSerializationContext? parent = null, JsonObject? json = null)
    : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = [];
    private readonly JsonObject _json = json ?? [];

    public ICoreSerializationContext? Parent { get; } = parent;

    public CoreSerializationMode Mode => CoreSerializationMode.ReadWrite;

    public Type OwnerType { get; } = ownerType;

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

    public bool Contains(string name)
    {
        return _json.ContainsKey(name);
    }
}
