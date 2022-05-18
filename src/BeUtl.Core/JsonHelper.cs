using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace BeUtl;

public static class JsonHelper
{
    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static void JsonSave(this IJsonSerializable serializable, string filename)
    {
        using var stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);

        serializable.ToJson().WriteTo(writer, SerializerOptions);
    }

    public static void JsonRestore(this IJsonSerializable serializable, string filename)
    {
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        var node = JsonNode.Parse(stream);

        if (node != null)
        {
            serializable.FromJson(node);
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
}
