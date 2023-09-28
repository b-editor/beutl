using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Operation;

public sealed class DummySourceOperator : SourceOperator, IDummy
{
    internal JsonObject? Json { get; set; }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        Json = json;
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        if (Json != null)
        {
            foreach (KeyValuePair<string, JsonNode?> item in Json)
            {
                if (item.Value == null)
                {
                    json[item.Key] = null;
                }
                else
                {
                    using var bufferWriter = new PooledArrayBufferWriter<byte>();
                    using var writer = new Utf8JsonWriter(bufferWriter);
                    item.Value.WriteTo(writer);

                    writer.Flush();

                    json[item.Key] = JsonNode.Parse(bufferWriter.WrittenSpan);
                }
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (Json != null)
        {
            foreach (KeyValuePair<string, JsonNode?> item in Json)
            {
                if (item.Value == null)
                {
                    context.SetValue<JsonNode?>(item.Key, null);
                }
                else
                {
                    using var bufferWriter = new PooledArrayBufferWriter<byte>();
                    using var writer = new Utf8JsonWriter(bufferWriter);
                    item.Value.WriteTo(writer);

                    writer.Flush();

                    context.SetValue<JsonNode?>(item.Key, JsonNode.Parse(bufferWriter.WrittenSpan));
                }
            }
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Json = (context as JsonSerializationContext)?.GetJsonObject();
    }
}
