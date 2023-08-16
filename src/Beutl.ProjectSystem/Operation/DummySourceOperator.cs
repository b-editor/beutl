using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Utilities;

namespace Beutl.Operation;

public sealed class DummySourceOperator : SourceOperator
{
    internal JsonObject? Json { get; set; }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        Json = json;
    }

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
}
