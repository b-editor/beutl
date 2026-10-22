using System.Collections;
using System.Reactive;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.IO;

namespace Beutl.Serialization;

public partial class JsonSerializationContext
{
    private static JsonNode? Serialize(
        object value, Type actualType, Type baseType, ICoreSerializationContext? parent)
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
            // A collection is written item by item unless its type has a JSON converter of its own. For a value
            // declared as object, System.Text.Json uses the converter of the runtime type, so that type is checked.
            case IEnumerable enumerable when !HasOwnJsonConverter(baseType == typeof(object) ? actualType : baseType):
                return SerializeEnumerable(enumerable, actualType, baseType, parent);
            default:
                return SerializeWithJsonSerializer(value, baseType);
        }
    }

    private static JsonNode? SerializeWithJsonSerializer(object value, Type baseType)
    {
        return JsonSerializer.SerializeToNode(value, baseType, JsonHelper.SerializerOptions);
    }

    // Whether System.Text.Json converts the type with a converter from the options or from [JsonConverter] on the
    // type itself (it does not look the attribute up on base types), rather than with a built-in one.
    private static bool HasOwnJsonConverter(Type type)
    {
        return JsonHelper.SerializerOptions.Converters.Any(converter => converter.CanConvert(type))
               || type.IsDefined(typeof(JsonConverterAttribute), inherit: false);
    }

    private static JsonNode? SerializeCoreSerializable(
        ICoreSerializable coreSerializable, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        SerializedObjectCapture.Record(coreSerializable);
        if (coreSerializable is CoreObject { Uri: not null } coreObject && parent != null)
        {
            return SerializeObjectFile(coreObject, parent);
        }

        var innerContext = new JsonSerializationContext(actualType, parent);

        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            coreSerializable.Serialize(innerContext);
            innerContext.WriteUnknownMembers(coreSerializable);
            if (coreSerializable is IFallback fallback)
            {
                innerContext.WriteFallback(fallback);
            }
        }

        JsonObject obj = innerContext.GetJsonObject();

        if (coreSerializable is not IFallback && !baseType.IsSealed)
        {
            obj.WriteDiscriminator(actualType);
        }

        return obj;
    }

    private static JsonNode? SerializeEnumerable(
        IEnumerable enumerable, Type actualType, Type baseType, ICoreSerializationContext? parent)
    {
        Type elementType = ArrayTypeHelpers.GetElementType(actualType) ?? typeof(object);

        if (TrySerializeDictionary(enumerable, actualType, baseType, parent, out JsonNode? result))
        {
            return result;
        }

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

        if (valueType.IsValueType)
        {
            result = SerializeWithJsonSerializer(enumerable, baseType);
            return true;
        }

        var jobj = new JsonObject();
        foreach (DictionaryEntry entry in (IDictionary)enumerable)
        {
            jobj[(string)entry.Key] = Serialize(entry.Value!, entry.Value!.GetType(), valueType, parent);
        }

        result = jobj;
        return true;
    }

    private static JsonArray SerializeArray(
        IEnumerable enumerable, Type elementType, ICoreSerializationContext? parent)
    {
        var jarray = new JsonArray();

        foreach (object? item in enumerable)
        {
            jarray.Add(Serialize(item, item.GetType(), elementType, parent));
        }

        return jarray;
    }

    internal static JsonNode SerializeObjectFile(CoreObject value, ICoreSerializationContext parent)
    {
        if (parent.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            CoreSerializer.StoreToUri(value, value.Uri!,
                CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);
        }

        Uri serializedUri = UriHelper.ToSerializedUri(value.Uri!, parent.BaseUri);

        if (parent.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects)
            && CoreSerializer.SerializeEmbeddedReference(value, serializedUri, parent) is { } node)
        {
            return node;
        }

        return (JsonValue)serializedUri.ToString();
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
        if (value is Unit)
        {
            RemoveValue(name);
            return;
        }

        if (value == null)
        {
            CaptureValue(name, value);
            _json[name] = null;
            return;
        }

        Type actualType = value.GetType();
        using var objects = CapturedValues is not null ? new SerializedObjectCapture() : null;
        _json[name] = Serialize(value, actualType, baseType, this);
        CaptureValue(name, value, objects);
    }

    private void RemoveValue(string name)
    {
        CapturedValues?.Remove(name);
        _json.Remove(name);
    }
}
