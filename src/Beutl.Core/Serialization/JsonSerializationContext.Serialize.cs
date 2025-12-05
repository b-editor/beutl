using System.Collections;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
    private static JsonNode? Serialize(
        string name, object value, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        if (value is string)
        {
            goto UseJsonSerializer;
        }
        else if (value is ICoreSerializable coreSerializable)
        {
            if (coreSerializable is CoreObject { Uri: not null } coreObject && parent != null)
            {
                return SerializeObjectFile(coreObject, parent);
            }

            var innerContext = new JsonSerializationContext(
                actualType,
                parent);

            using (ThreadLocalSerializationContext.Enter(innerContext))
            {
                coreSerializable.Serialize(innerContext);
            }

            JsonObject obj = innerContext.GetJsonObject();
            if (value is not IDummy && !baseType.IsSealed)
            {
                obj.WriteDiscriminator(actualType);
            }

            return obj;
        }
        else if (value is IReference reference)
        {
            return reference.Id;
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
                            innerName, typed.Value, typed.Value.GetType(), valueType, parent);
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
                        innerName, item, item.GetType(), elementType, parent));

                    index++;
                }

                return jarray;
            }
        }

        UseJsonSerializer:
        return JsonSerializer.SerializeToNode(value, baseType, JsonHelper.SerializerOptions);
    }

    private static JsonNode SerializeObjectFile(
        CoreObject value, ICoreSerializationContext parent)
    {
        if (parent.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            var node = CoreSerializer.SerializeToJsonObject(value,
                new CoreSerializerOptions { BaseUri = value.Uri });

            using var stream = File.Create(Uri.UnescapeDataString(value.Uri!.LocalPath));
            using var innerWriter = new Utf8JsonWriter(stream, JsonHelper.WriterOptions);
            node.WriteTo(innerWriter);
        }

        var serializedUri = value.Uri!;
        if (parent.BaseUri?.Scheme == value.Uri!.Scheme)
        {
            serializedUri = parent.BaseUri.MakeRelativeUri(value.Uri);
        }

        if (parent.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
        {
            var node = CoreSerializer.SerializeToJsonObject(value,
                new CoreSerializerOptions { BaseUri = value.Uri });
            node["Uri"] = serializedUri.ToString();
            return node;
        }
        else
        {
            return (JsonValue)serializedUri.ToString();
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
            if (value is ICoreSerializable or IEnumerable or IReference)
            {
                _json[name] = Serialize(name, value, actualType, typeof(T), this);
            }
            else if (value is JsonNode jsonNode)
            {
                _json[name] = jsonNode;
            }
            else
            {
                _json[name] = JsonSerializer.SerializeToNode(value, JsonHelper.SerializerOptions);
            }

            _knownTypes[name] = (typeof(T), actualType);
        }
    }

    public void SetValue(string name, object? value, Type type)
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
            if (value is ICoreSerializable or IEnumerable or IReference)
            {
                _json[name] = Serialize(name, value, actualType, type, this);
            }
            else if (value is JsonNode jsonNode)
            {
                _json[name] = jsonNode;
            }
            else
            {
                _json[name] = JsonSerializer.SerializeToNode(value, type, JsonHelper.SerializerOptions);
            }

            _knownTypes[name] = (type, actualType);
        }
    }
}
