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
            if (LocalSerializationErrorNotifier.Current is not { } notifier)
            {
                notifier = NullSerializationErrorNotifier.Instance;
            }
            ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

            var context = new JsonSerializationContext(typeToConvert, notifier, parent, jsonObject);

            Type? actualType = typeToConvert.IsSealed ? typeToConvert : jsonObject.GetDiscriminator(typeToConvert);
            if (actualType?.IsAssignableTo(typeToConvert) == true
                && Activator.CreateInstance(actualType) is ICoreSerializable instance)
            {
                using (ThreadLocalSerializationContext.Enter(context))
                {
                    instance.Deserialize(context);
                }

                return instance;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
        if (LocalSerializationErrorNotifier.Current is not { } notifier)
        {
            notifier = NullSerializationErrorNotifier.Instance;
        }

        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;
        Type valueType = value.GetType();
        var context = new JsonSerializationContext(value.GetType(), notifier, parent);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            value.Serialize(context);
        }

        JsonObject obj = context.GetJsonObject();
        obj.WriteDiscriminator(valueType);
        obj.WriteTo(writer, options);
    }
}
