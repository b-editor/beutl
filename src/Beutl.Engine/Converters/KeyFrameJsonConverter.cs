using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl.Converters;

internal sealed class KeyFrameJsonConverter : JsonConverter<IKeyFrame>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IKeyFrame));
    }

    public override IKeyFrame Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            var instance = (IKeyFrame)CoreSerializer.DeserializeFromJsonObject(jsonObject, typeToConvert)!;
            return instance;
        }

        throw new Exception("Invalid IKeyFrame");
    }

    public override void Write(Utf8JsonWriter writer, IKeyFrame value, JsonSerializerOptions options)
    {
        JsonObject obj = CoreSerializer.SerializeToJsonObject(value);
        obj.WriteTo(writer, options);
    }
}
