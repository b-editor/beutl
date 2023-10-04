using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
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

                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        if (Activator.CreateInstance(actualType) is ICoreSerializable instance)
                        {
                            instance.Deserialize(context);

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
                        output, jarray, elementType,
                        new RelaySerializationErrorNotifier(errorNotifier, propertyName), parent);

                    return ArrayTypeHelpers.ConvertArrayType(output, baseType, elementType);
                }
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
}
