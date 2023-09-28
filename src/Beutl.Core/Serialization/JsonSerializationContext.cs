using System.Collections;
using System.Reactive;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public class JsonSerializationContext : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = new();
    private readonly JsonObject _json;

    public JsonSerializationContext(
        Type ownerType, ISerializationErrorNotifier errorNotifier,
        ICoreSerializationContext? parent = null, JsonObject? json = null)
    {
        OwnerType = ownerType;
        Parent = parent;
        ErrorNotifier = errorNotifier;
        _json = json ?? new JsonObject();
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
            if (baseType.IsAssignableTo(typeof(IDictionary))
                && ArrayTypeHelpers.GetEntryType(baseType) is (Type keyType, Type valueType)
                && keyType == typeof(string))
            {
                var output = new List<KeyValuePair<string, object?>>(obj.Count);
                foreach (KeyValuePair<string, JsonNode?> item in obj)
                {
                    string name = item.Key;
                    if (item.Value == null)
                    {
                        output.Add(new(name, DefaultValueHelpers.GetDefault(valueType)));
                    }
                    else
                    {
                        object? valueNode = Deserialize(
                            item.Value, valueType, name,
                            new RelaySerializationErrorNotifier(errorNotifier, name), parent);
                        output.Add(new(name, valueNode));
                    }
                }

                return ArrayTypeHelpers.ConvertDictionaryType(output, baseType, valueType);
            }

            Type? actualType = baseType.IsSealed ? baseType : obj.GetDiscriminator(baseType);
            if (actualType?.IsAssignableTo(typeof(ICoreSerializable)) == true)
            {
                var context = new JsonSerializationContext(
                    ownerType: actualType,
                    errorNotifier: new RelaySerializationErrorNotifier(errorNotifier, propertyName),
                    parent: parent,
                    json: obj);

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
        if (value is string)
        {
            goto UseJsonSerializer;
        }
        else if (value is ICoreSerializable coreSerializable)
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

            // 'Dictionary<string, U>' の場合、JsonObjectを返す
            if (actualType.IsAssignableTo(typeof(IDictionary))
                && ArrayTypeHelpers.GetEntryType(actualType) is (Type keyType, Type valueType)
                && keyType == typeof(string))
            {
                if (!valueType.IsValueType)
                {
                    var jobj = new JsonObject();
                    foreach (object? item in enm)
                    {
                        StringValuePair<object> typed = Unsafe.As<StringValuePair<object>>(item);
                        string innerName = typed.Key;

                        jobj[innerName] = Serialize(
                            innerName, typed.Value, typed.Value.GetType(), valueType,
                            new RelaySerializationErrorNotifier(errorNotifier, innerName), parent);
                    }

                    return jobj;
                }
                else
                {
                    goto UseJsonSerializer;
                }
            }
            else
            {
                var jarray = new JsonArray();
                int index = 0;
                foreach (object? item in enm)
                {
                    string innerName = index.ToString();
                    jarray.Add(Serialize(
                        innerName, item, item.GetType(), elementType,
                        new RelaySerializationErrorNotifier(errorNotifier, innerName), parent));

                    index++;
                }

                return jarray;
            }
        }

    UseJsonSerializer:
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

    public void Populate(string name, ICoreSerializable obj)
    {
        if (_json[name] is JsonObject jobj)
        {
            var context = new JsonSerializationContext(
                ownerType: obj.GetType(),
                errorNotifier: new RelaySerializationErrorNotifier(ErrorNotifier, name),
                parent: this,
                json: jobj);

            obj.Deserialize(context);
        }
    }
}
