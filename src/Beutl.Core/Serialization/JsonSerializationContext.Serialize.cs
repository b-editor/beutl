using System.Collections;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
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

            using (ThreadLocalSerializationContext.Enter(innerContext))
            {
                coreSerializable.Serialize(innerContext);
            }

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
        if (value is Unit)
        {
            _json.Remove(name);
            _knownTypes.Remove(name);
        }
        else if (value == null)
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
