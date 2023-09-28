using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.Serialization;

public sealed class CoreSerializableJsonConverter : JsonConverter<ICoreSerializable>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(ICoreSerializable));
    }

    public override ICoreSerializable? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                && Activator.CreateInstance(actualType) is ICoreSerializable instance)
            {
                instance.Deserialize(context);

                return instance;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
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
        value.Serialize(context);

        JsonObject obj = context.GetJsonObject();
        obj.WriteDiscriminator(valueType);
        obj.WriteTo(writer, options);
    }
}
