using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Graphics.Transformation;
using Beutl.Serialization;

namespace Beutl.Converters;

internal sealed class TransformJsonConverter : JsonConverter<ITransform>
{
    public override ITransform Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            if (LocalSerializationErrorNotifier.Current is { } notifier)
            {
                notifier = new RelaySerializationErrorNotifier(notifier, "[Unknown]");
            }
            else
            {
                notifier = NullSerializationErrorNotifier.Instance;
            }

            var context = new JsonSerializationContext(typeToConvert, notifier, json: jsonObject);

            Type? actualType = typeToConvert.IsSealed ? typeToConvert : jsonObject.GetDiscriminator(typeToConvert);
            if (actualType?.IsAssignableTo(typeToConvert) == true
                && Activator.CreateInstance(actualType) is ICoreSerializable instance
                && instance is ITransform transform)
            {
                instance.Deserialize(context);

                return transform;
            }
        }

        throw new Exception("Invalid Transform");
    }

    public override void Write(Utf8JsonWriter writer, ITransform value, JsonSerializerOptions options)
    {
        if (value is not ICoreSerializable serializable) return;

        if (LocalSerializationErrorNotifier.Current is { } notifier)
        {
            notifier = new RelaySerializationErrorNotifier(notifier, "[Unknown]");
        }
        else
        {
            notifier = NullSerializationErrorNotifier.Instance;
        }

        Type valueType = value.GetType();
        var context = new JsonSerializationContext(value.GetType(), notifier);
        serializable.Serialize(context);

        JsonObject obj = context.GetJsonObject();
        obj.WriteDiscriminator(valueType);
        obj.WriteTo(writer, options);
    }
}
