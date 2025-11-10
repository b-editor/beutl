using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.Serialization;

public sealed class CoreSerializableJsonConverter : JsonConverter<ICoreSerializable>
{
    public override ICoreSerializable? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            return CoreSerializer.DeserializeFromJsonObject(jsonObject, typeToConvert) as ICoreSerializable;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
        JsonObject obj = CoreSerializer.SerializeToJsonObject(value);
        obj.WriteTo(writer, options);
    }
}
