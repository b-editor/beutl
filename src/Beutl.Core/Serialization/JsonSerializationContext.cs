using System.Collections;
using System.Reactive;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public class JsonSerializationContext : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = new();
    private readonly JsonObject _json = new();

    public JsonSerializationContext(Type ownerType, ISerializationErrorNotifier errorNotifier, ICoreSerializationContext? parent = null)
    {
        OwnerType = ownerType;
        Parent = parent;
        ErrorNotifier = errorNotifier;
    }

    public ICoreSerializationContext? Parent { get; }

    public CoreSerializationMode Mode => CoreSerializationMode.ReadWrite;

    public Type OwnerType { get; }

    public ISerializationErrorNotifier ErrorNotifier { get; }

    public JsonObject GetJsonObject()
    {
        return _json;
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

    private static void DeserializeArray(
        List<object?> output, JsonArray jarray, Type elementType,
        ISerializationErrorNotifier errorNotifier, ICoreSerializationContext? parent)
    {
        int index = 0;
        foreach (JsonNode? item in jarray)
        {
            if (item == null)
            {
                output.Add(DefaultValueHelpers.GetDefault(elementType));
            }
            else
            {
                string name = index.ToString();
                output.Add(Deserialize(item, elementType, name, new RelaySerializationErrorNotifier(errorNotifier, name), parent));
            }

            index++;
        }
    }

    private static object? Deserialize(
        JsonNode node, Type baseType, string propertyName,
        ISerializationErrorNotifier errorNotifier, ICoreSerializationContext? parent)
    {
        if (node is JsonObject obj)
        {
            Type? actualType = baseType.IsSealed ? baseType : obj.GetDiscriminator(baseType);
            if (actualType?.IsAssignableTo(typeof(ICoreSerializable)) == true)
            {
                var context = new JsonSerializationContext(
                    actualType,
                    new RelaySerializationErrorNotifier(errorNotifier, propertyName),
                    parent);

                if (Activator.CreateInstance(actualType) is ICoreSerializable instance)
                {
                    instance.Deserialize(context);

                    return instance;
                }
            }
        }
        else if (node is JsonArray jarray)
        {
            // 要素の型を決定
            Type? elementType = ArrayTypeHelpers.GetElementType(baseType);

            if (elementType != null)
            {
                var output = new List<object?>(jarray.Count);
                DeserializeArray(
                    output, jarray, elementType,
                    new RelaySerializationErrorNotifier(errorNotifier, propertyName), parent);

                return ArrayTypeHelpers.ConvertArrayType(output, baseType, elementType);
            }
        }

        ISerializationErrorNotifier? captured = LocalSerializationErrorNotifier.Current;
        try
        {
            LocalSerializationErrorNotifier.Current = new RelaySerializationErrorNotifier(errorNotifier, propertyName);
            return JsonSerializer.Deserialize(node, baseType, JsonHelper.SerializerOptions);
        }
        finally
        {
            LocalSerializationErrorNotifier.Current = captured;
        }
    }

    public T? GetValue<T>(string name)
    {
        // TにはOptional型が入る場合があるので、Jsonプロパティがnullの場合と存在しない場合で分ける
        Type baseType = typeof(T);
        if (_json.TryGetPropertyValue(name, out JsonNode? node))
        {
            if (node == null)
            {
                return DefaultValueHelpers.DefaultOrOptional<T>();
            }
            else
            {
                return (T?)Deserialize(node, baseType, name, ErrorNotifier, this);
            }
        }
        else
        {
            // 存在しない場合
            return default;
        }
    }

    private static JsonNode? Serialize(
        string name, object value, Type actualType, Type baseType,
        ISerializationErrorNotifier errorNotifier, ICoreSerializationContext? parent)
    {
        if (value is ICoreSerializable coreSerializable)
        {
            var innerContext = new JsonSerializationContext(
                actualType,
                new RelaySerializationErrorNotifier(errorNotifier, name),
                parent);

            coreSerializable.Serialize(innerContext);
            JsonObject obj = innerContext.GetJsonObject();
            if (value is not IDummy && actualType != baseType)
            {
                obj.WriteDiscriminator(actualType);
            }

            return obj;
        }
        else if (value is JsonNode jsonNode)
        {
            return jsonNode;
        }
        else if (value is IEnumerable enm)
        {
            Type elementType = ArrayTypeHelpers.GetElementType(actualType) ?? typeof(object);

            var jarray = new JsonArray();
            int index = 0;
            foreach (object? item in enm)
            {
                string innerName = index.ToString();
                Serialize(
                    innerName, item, item.GetType(), elementType,
                    new RelaySerializationErrorNotifier(errorNotifier, innerName), parent);

                index++;
            }

            return jarray;
        }
        else
        {
            ISerializationErrorNotifier? captured = LocalSerializationErrorNotifier.Current;
            try
            {
                LocalSerializationErrorNotifier.Current = new RelaySerializationErrorNotifier(errorNotifier, name);
                return JsonSerializer.SerializeToNode(value, baseType, JsonHelper.SerializerOptions);
            }
            finally
            {
                LocalSerializationErrorNotifier.Current = captured;
            }
        }
    }

    public void SetValue<T>(string name, T? value)
    {
        if (value is Unit || value == null)
        {
            _json[name] = null;
            _knownTypes.Remove(name);
        }
        else
        {
            Type actualType = value.GetType();
            if (value is ICoreSerializable or IEnumerable)
            {
                _json[name] = Serialize(name, value, actualType, typeof(T), ErrorNotifier, this);
            }
            else if (value is JsonNode jsonNode)
            {
                _json[name] = jsonNode;
            }
            else
            {
                ISerializationErrorNotifier? captured = LocalSerializationErrorNotifier.Current;
                try
                {
                    LocalSerializationErrorNotifier.Current = new RelaySerializationErrorNotifier(ErrorNotifier, name);
                    _json[name] = JsonSerializer.SerializeToNode(value, JsonHelper.SerializerOptions);
                }
                finally
                {
                    LocalSerializationErrorNotifier.Current = captured;
                }
            }

            _knownTypes[name] = (typeof(T), actualType);
        }
    }
}
