using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.JsonConverters;

namespace Beutl;

public static class JsonHelper
{
    private static readonly Dictionary<Type, JsonConverter> s_converters = new();

    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters =
        {
            new CultureInfoConverter(),
            new DirectoryInfoConverter(),
            new FileInfoConverter()
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
        using var stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);
        JsonNode json = new JsonObject();

        serializable.WriteToJson(ref json);
        json.WriteTo(writer, SerializerOptions);
    }

    public static void JsonRestore(this IJsonSerializable serializable, string filename)
    {
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        var node = JsonNode.Parse(stream);

        if (node != null)
        {
            serializable.ReadFromJson(node);
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
}
