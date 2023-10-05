using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Converters;

internal sealed class PenJsonConverter : JsonConverter<IPen>
{
    public override IPen Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            if (LocalSerializationErrorNotifier.Current is not { } notifier)
            {
                notifier = NullSerializationErrorNotifier.Instance;
            }

            ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;
            var context = new JsonSerializationContext(typeToConvert, notifier, parent, jsonObject);

            var pen = new Pen();
            using (ThreadLocalSerializationContext.Enter(context))
            {
                pen.Deserialize(context);
            }

            return pen;
        }

        throw new Exception("Invalid Pen");
    }

    public override void Write(Utf8JsonWriter writer, IPen value, JsonSerializerOptions options)
    {
        if (value is not ICoreSerializable serializable) return;

        if (LocalSerializationErrorNotifier.Current is not { } notifier)
        {
            notifier = NullSerializationErrorNotifier.Instance;
        }

        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;
        var context = new JsonSerializationContext(value.GetType(), notifier, parent);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            serializable.Serialize(context);
        }

        JsonObject obj = context.GetJsonObject();
        obj.WriteTo(writer, options);
    }
}
