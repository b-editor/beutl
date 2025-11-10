using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
    private static void DeserializeArray(
        List<object?> output, JsonArray jarray, Type elementType, ICoreSerializationContext? parent)
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
                output.Add(Deserialize(item, elementType, name, parent));
            }

            index++;
        }
    }

    private static object? Deserialize(
        JsonNode node, Type baseType, string propertyName, ICoreSerializationContext? parent)
    {
        if (!baseType.IsAssignableTo(typeof(JsonNode)))
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
                                item.Value, valueType, name, parent);
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
                        parent: parent,
                        json: obj);

                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        if (Activator.CreateInstance(actualType) is ICoreSerializable instance)
                        {
                            instance.Deserialize(context);
                            context.AfterDeserialized(instance);

                            return instance;
                        }
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
                        output, jarray, elementType, parent);

                    return ArrayTypeHelpers.ConvertArrayType(output, baseType, elementType);
                }
            }
            else if (node is JsonValue jsonValue
                     && jsonValue.TryGetValue(out Guid id)
                     && baseType.IsAssignableTo(typeof(IReference)))
            {
                return Activator.CreateInstance(baseType, id);
            }
        }

        return JsonSerializer.Deserialize(node, baseType, JsonHelper.SerializerOptions);
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
                return (T?)Deserialize(node, baseType, name, this);
            }
        }
        else
        {
            // 存在しない場合
            return default;
        }
    }

    public object? GetValue(string name, Type type)
    {
        // TにはOptional型が入る場合があるので、Jsonプロパティがnullの場合と存在しない場合で分ける
        if (_json.TryGetPropertyValue(name, out JsonNode? node))
        {
            if (node == null)
            {
                return null;
            }
            else
            {
                return Deserialize(node, type, name, this);
            }
        }
        else
        {
            // 存在しない場合
            return default;
        }
    }
}
