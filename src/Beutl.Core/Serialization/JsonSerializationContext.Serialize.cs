using System.Collections;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.IO;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
    private static JsonNode? Serialize(
        string name, object value, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        switch (value)
        {
            case string or IFileSource:
                return SerializeWithJsonSerializer(value, baseType);
            case ICoreSerializable coreSerializable:
                return SerializeCoreSerializable(coreSerializable, actualType, baseType, parent);
            case IReference reference:
                return reference.Id;
            case JsonNode jsonNode:
                return jsonNode;
            case IEnumerable enumerable:
                return SerializeEnumerable(enumerable, actualType, baseType, parent);
            default:
                return SerializeWithJsonSerializer(value, baseType);
        }
    }

    private static JsonNode? SerializeWithJsonSerializer(object value, Type baseType)
    {
        return JsonSerializer.SerializeToNode(value, baseType, JsonHelper.SerializerOptions);
    }

    private static JsonNode? SerializeCoreSerializable(
        ICoreSerializable coreSerializable, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        // 外部ファイル参照として保存するケース
        if (coreSerializable is CoreObject { Uri: not null } coreObject && parent != null)
        {
            return SerializeObjectFile(coreObject, parent);
        }

        var innerContext = new JsonSerializationContext(actualType, parent);

        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            coreSerializable.Serialize(innerContext);
        }

        JsonObject obj = innerContext.GetJsonObject();

        // 型判別子を書き込む（Dummyでなく、sealedでない場合）
        if (coreSerializable is not IDummy && !baseType.IsSealed)
        {
            obj.WriteDiscriminator(actualType);
        }

        return obj;
    }

    private static JsonNode? SerializeEnumerable(
        IEnumerable enumerable, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        Type elementType = ArrayTypeHelpers.GetElementType(actualType) ?? typeof(object);

        // Dictionary<string, T> の場合
        if (TrySerializeDictionary(enumerable, actualType, baseType, parent, out JsonNode? result))
        {
            return result;
        }

        // 通常の配列/リスト
        return SerializeArray(enumerable, elementType, parent);
    }

    private static bool TrySerializeDictionary(
        IEnumerable enumerable,
        Type actualType,
        Type baseType,
        ICoreSerializationContext? parent,
        out JsonNode? result)
    {
        result = null;

        if (!actualType.IsAssignableTo(typeof(IDictionary)))
        {
            return false;
        }

        if (ArrayTypeHelpers.GetEntryType(actualType) is not (Type keyType, Type valueType))
        {
            return false;
        }

        if (keyType != typeof(string))
        {
            return false;
        }

        // 値型の場合はJsonSerializerに委譲
        if (valueType.IsValueType)
        {
            result = SerializeWithJsonSerializer(enumerable, baseType);
            return true;
        }

        var jobj = new JsonObject();
        foreach (object? item in enumerable)
        {
            StringValuePair<object> typed = Unsafe.As<StringValuePair<object>>(item);
            jobj[typed.Key] = Serialize(typed.Key, typed.Value, typed.Value.GetType(), valueType, parent);
        }

        result = jobj;
        return true;
    }

    private static JsonArray SerializeArray(
        IEnumerable enumerable, Type elementType, ICoreSerializationContext? parent)
    {
        var jarray = new JsonArray();
        int index = 0;

        foreach (object? item in enumerable)
        {
            string innerName = index.ToString();
            jarray.Add(Serialize(innerName, item, item.GetType(), elementType, parent));
            index++;
        }

        return jarray;
    }

    private static JsonNode SerializeObjectFile(CoreObject value, ICoreSerializationContext parent)
    {
        // 参照オブジェクトを保存
        if (parent.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            SaveObjectToFile(value);
        }

        Uri serializedUri = ResolveSerializationUri(value.Uri!, parent);

        // 埋め込みモードの場合はオブジェクト全体を含める
        if (parent.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
        {
            return CreateEmbeddedObjectNode(value, serializedUri);
        }

        return (JsonValue)serializedUri.ToString();
    }

    private static void SaveObjectToFile(CoreObject value)
    {
        var node = CoreSerializer.SerializeToJsonObject(
            value,
            new CoreSerializerOptions { BaseUri = value.Uri });

        using var stream = File.Create(value.Uri!.LocalPath);
        using var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions);
        node.WriteTo(writer);
    }

    private static Uri ResolveSerializationUri(Uri objectUri, ICoreSerializationContext parent)
    {
        if (parent.BaseUri?.Scheme == objectUri.Scheme)
        {
            return parent.BaseUri.MakeRelativeUri(objectUri);
        }

        return objectUri;
    }

    private static JsonObject CreateEmbeddedObjectNode(CoreObject value, Uri serializedUri)
    {
        var node = CoreSerializer.SerializeToJsonObject(
            value,
            new CoreSerializerOptions { BaseUri = value.Uri });
        node["Uri"] = serializedUri.ToString();
        return node;
    }

    public void SetValue<T>(string name, T? value)
    {
        SetValueCore(name, value, typeof(T));
    }

    public void SetValue(string name, object? value, Type type)
    {
        SetValueCore(name, value, type);
    }

    private void SetValueCore(string name, object? value, Type baseType)
    {
        // Unit型は削除
        if (value is Unit)
        {
            RemoveValue(name);
            return;
        }

        // nullの場合
        if (value == null)
        {
            _json[name] = null;
            _knownTypes.Remove(name);
            return;
        }

        Type actualType = value.GetType();
        _json[name] = SerializeValue(value, actualType, baseType);
        _knownTypes[name] = (baseType, actualType);
    }

    private JsonNode? SerializeValue(object value, Type actualType, Type baseType)
    {
        if (value is ICoreSerializable or IEnumerable or IReference)
        {
            return Serialize(string.Empty, value, actualType, baseType, this);
        }

        if (value is JsonNode jsonNode)
        {
            return jsonNode;
        }

        return JsonSerializer.SerializeToNode(value, baseType, JsonHelper.SerializerOptions);
    }

    private void RemoveValue(string name)
    {
        _json.Remove(name);
        _knownTypes.Remove(name);
    }
}
