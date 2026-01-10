using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.IO;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
    private static object? Deserialize(
        JsonNode node, Type baseType, string propertyName, ICoreSerializationContext? parent)
    {
        // JsonNode型として要求されている場合はそのまま返す
        if (baseType.IsAssignableTo(typeof(JsonNode)))
        {
            return DeserializeWithJsonSerializer(node, baseType);
        }

        return node switch
        {
            JsonObject obj => DeserializeObject(obj, baseType, parent),
            JsonArray jarray => DeserializeArray(jarray, baseType, parent),
            JsonValue jsonValue => DeserializeValue(jsonValue, baseType, parent),
            _ => DeserializeWithJsonSerializer(node, baseType)
        };
    }

    private static object? DeserializeWithJsonSerializer(JsonNode node, Type baseType)
    {
        return node.Deserialize(baseType, JsonHelper.SerializerOptions);
    }

    private static object? DeserializeObject(
        JsonObject obj, Type baseType, ICoreSerializationContext? parent)
    {
        // Dictionary<string, T> の場合
        if (TryDeserializeDictionary(obj, baseType, parent, out object? result))
        {
            return result;
        }

        // ICoreSerializable の場合
        if (TryDeserializeCoreSerializable(obj, baseType, parent, out result))
        {
            return result;
        }

        return DeserializeWithJsonSerializer(obj, baseType);
    }

    private static bool TryDeserializeDictionary(
        JsonObject obj, Type baseType, ICoreSerializationContext? parent, out object? result)
    {
        result = null;

        if (!baseType.IsAssignableTo(typeof(IDictionary)))
            return false;

        if (ArrayTypeHelpers.GetEntryType(baseType) is not (Type keyType, Type valueType))
            return false;

        if (keyType != typeof(string))
            return false;

        var output = new List<KeyValuePair<string, object?>>(obj.Count);

        foreach (KeyValuePair<string, JsonNode?> item in obj)
        {
            object? value = item.Value == null
                ? DefaultValueHelpers.GetDefault(valueType)
                : Deserialize(item.Value, valueType, item.Key, parent);

            output.Add(new(item.Key, value));
        }

        result = ArrayTypeHelpers.ConvertDictionaryType(output, baseType, valueType);
        return true;
    }

    private static bool TryDeserializeCoreSerializable(
        JsonObject obj, Type baseType, ICoreSerializationContext? parent, out object? result)
    {
        result = null;

        Type? actualType = baseType.IsSealed ? baseType : obj.GetDiscriminator(baseType);

        if (actualType?.IsAssignableTo(typeof(ICoreSerializable)) != true) return false;

        var instance = Activator.CreateInstance(actualType) as ICoreSerializable
                       ?? throw new InvalidOperationException(
                           $"Could not create instance of type {actualType.FullName}.");

        CoreSerializerOptions? options = null;
        CoreSerializer.ReflectUri(obj, instance, parent, ref options);

        var context = new JsonSerializationContext(
            ownerType: actualType,
            parent: parent,
            json: obj,
            options: options);

        using (ThreadLocalSerializationContext.Enter(context))
        {
            instance.Deserialize(context);
            context.AfterDeserialized(instance);
        }

        result = instance;
        return true;
    }

    private static object? DeserializeArray(
        JsonArray jarray, Type baseType, ICoreSerializationContext? parent)
    {
        Type? elementType = ArrayTypeHelpers.GetElementType(baseType);

        if (elementType == null)
        {
            return DeserializeWithJsonSerializer(jarray, baseType);
        }

        var output = new List<object?>(jarray.Count);
        DeserializeArrayElements(output, jarray, elementType, parent);

        return ArrayTypeHelpers.ConvertArrayType(output, baseType, elementType);
    }

    private static void DeserializeArrayElements(
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
                output.Add(Deserialize(item, elementType, index.ToString(), parent));
            }

            index++;
        }
    }

    private static object? DeserializeValue(
        JsonValue jsonValue, Type baseType, ICoreSerializationContext? parent)
    {
        // IReference の場合
        if (jsonValue.TryGetValue(out Guid id) && baseType.IsAssignableTo(typeof(IReference)))
        {
            return Activator.CreateInstance(baseType, id);
        }

        // 外部ファイル参照の ICoreSerializable の場合
        // IFileSourceを実装している場合はJsonConverterで処理されるのでここでは処理しない
        if (jsonValue.TryGetValue(out string? uriString)
            && typeof(ICoreSerializable).IsAssignableFrom(baseType)
            && !typeof(IFileSource).IsAssignableFrom(baseType))
        {
            return DeserializeObjectFile(uriString, baseType, parent);
        }

        return DeserializeWithJsonSerializer(jsonValue, baseType);
    }

    private static object DeserializeObjectFile(
        string? uriString, Type type, ICoreSerializationContext? parent)
    {
        Uri uri = ResolveUri(uriString, parent);
        return CoreSerializer.RestoreFromUri(uri, type);
    }

    private static Uri ResolveUri(string? uriString, ICoreSerializationContext? parent)
    {
        uriString = uriString != null ? Uri.UnescapeDataString(uriString) : null;

        if (!Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out Uri? uri))
            throw new JsonException($"Invalid URI: {uriString}");

        if (uri.IsAbsoluteUri)
            return uri;

        if (parent == null)
            throw new JsonException("Cannot resolve relative URI without a parent context.");

        if (!Uri.TryCreate(parent.BaseUri, uriString, out uri))
            throw new JsonException($"Invalid relative URI: {uriString}");

        return uri;
    }

    public T? GetValue<T>(string name)
    {
        var value = GetValueCore(name, typeof(T), useOptionalDefault: true);
        return value is T t ? t : default;
    }

    public object? GetValue(string name, Type type)
    {
        return GetValueCore(name, type, useOptionalDefault: false);
    }

    private object? GetValueCore(string name, Type type, bool useOptionalDefault)
    {
        // TにはOptional型が入る場合があるので、Jsonプロパティがnullの場合と存在しない場合で分ける
        if (!_json.TryGetPropertyValue(name, out JsonNode? node))
        {
            // プロパティが存在しない場合
            return null;
        }

        if (node == null)
        {
            // プロパティがnullの場合
            return useOptionalDefault
                ? DefaultValueHelpers.GetDefaultOrOptional(type)
                : null;
        }

        return Deserialize(node, type, name, this);
    }
}
