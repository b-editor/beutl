using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.JsonConverters;
using Beutl.Serialization;

namespace Beutl;

public static class JsonHelper
{
    private static readonly Dictionary<Type, JsonConverter> s_converters = new();

    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new CultureInfoConverter(),
            new DirectoryInfoConverter(),
            new FileInfoConverter(),
            new CoreSerializableJsonConverter(),
            new CoreObjectJsonConverter()
        }
    };

    public static JsonConverter GetOrCreateConverterInstance(Type converterType)
    {
        if (s_converters.TryGetValue(converterType, out var converter))
        {
            return converter;
        }
        else
        {
            converter = (JsonConverter)Activator.CreateInstance(converterType)!;
            s_converters.Add(converterType, converter);
            return converter;
        }
    }

    public static void JsonSave(this IJsonSerializable serializable, string filename)
    {
        var json = new JsonObject();

        serializable.WriteToJson(json);

        using var stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);
        json.WriteTo(writer, SerializerOptions);
    }

    public static void JsonRestore(this IJsonSerializable serializable, string filename)
    {
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        var node = JsonNode.Parse(stream);

        if (node is JsonObject obj)
        {
            serializable.ReadFromJson(obj);
        }
    }

    public static void JsonSave(this JsonNode node, string filename)
    {
        using var stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);

        node.WriteTo(writer, SerializerOptions);
    }

    public static JsonNode? JsonRestore(string filename)
    {
        if (!File.Exists(filename)) return null;
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonNode.Parse(stream);
    }

    // Todo: 未知の型の場合の処理
    public static Type? GetDiscriminator(this JsonNode node)
    {
        return node.TryGetDiscriminator(out Type? type) ? type : null;
    }

    public static Type? GetDiscriminator(this JsonNode node, Type baseType)
    {
        if(node.TryGetDiscriminator(out Type? type))
        {
            return type;
        }
        else
        {
            if (Attribute.GetCustomAttribute(baseType, typeof(DummyTypeAttribute)) is DummyTypeAttribute att)
            {
                return att.DummyType;
            }
            else
            {
                return null;
            }
        }
    }

    // Todo: 未知の型の場合の処理
    public static bool TryGetDiscriminator(this JsonNode node, [NotNullWhen(true)] out Type? type)
    {
        type = null;
        if (node is JsonObject obj)
        {
            JsonNode? typeNode = obj.TryGetPropertyValue("$type", out JsonNode? typeNode1) ? typeNode1
                               : obj.TryGetPropertyValue("@type", out JsonNode? typeNode2) ? typeNode2
                               : null;

            if (typeNode is JsonValue typeValue
                && typeValue.TryGetValue(out string? typeStr)
                && !string.IsNullOrWhiteSpace(typeStr))
            {
                type = TypeFormat.ToType(typeStr);
            }
        }

        return type != null;
    }

    public static void WriteDiscriminator(this JsonNode obj, Type type)
    {
        obj["$type"] = TypeFormat.ToString(type);
    }

    private static Dictionary<string, object> ParseJson(string json)
    {
        Dictionary<string, JsonElement> dic = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        return dic.ToDictionary(x => x.Key, x => ParseJsonElement(x.Value)!);
    }

    private static object? ParseJsonElement(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString(),
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.Array => elem.EnumerateArray().Select(e => ParseJsonElement(e)).ToArray(),
            JsonValueKind.Null => null,
            JsonValueKind.Object => ParseJson(elem.GetRawText()),
            _ => throw new NotSupportedException(),
        };
    }

    public static Dictionary<string, object> ToDictionary(this JsonNode json)
    {
        Dictionary<string, JsonElement> dic = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        return dic.ToDictionary(x => x.Key, x => ParseJsonElement(x.Value)!);
    }

    public static bool TryGetPropertyValueAsJsonValue<T>(this JsonObject obj, string propertyName, [NotNullWhen(true)] out T? value)
    {
        value = default;
        return obj.TryGetPropertyValue(propertyName, out var node)
            && node is JsonValue val
            && val.TryGetValue(out value);
    }
}
