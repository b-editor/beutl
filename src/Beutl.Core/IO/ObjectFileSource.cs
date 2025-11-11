using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.IO;

public class ObjectFileSource<T> : IFileSource
    where T : class, ICoreSerializable
{
    public bool IsBlob => false;

    public T? Object { get; set; }

    public Uri Uri
    {
        get => field ?? throw new InvalidOperationException("URI is not set.");
        set;
    }

    public void WriteTo(Stream stream)
    {
        if (Object == null) return;

        var node = CoreSerializer.SerializeToJsonObject(Object, new CoreSerializerOptions { BaseUri = Uri });

        using var writer = new Utf8JsonWriter(stream);
        node.WriteTo(writer);
    }

    public void ReadFrom(Stream stream, Uri uri)
    {
        using (stream)
        {
            Uri = uri;
            var node = JsonNode.Parse(stream);
            if (node is JsonObject jsonObject)
            {
                Object = CoreSerializer.DeserializeFromJsonObject(
                    jsonObject, typeof(T), new CoreSerializerOptions { BaseUri = Uri }) as T;
            }
        }
    }
}
