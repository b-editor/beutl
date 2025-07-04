using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Audio.Effects;
using Beutl.Serialization;

namespace Beutl.Audio.JsonConverters;

/// <summary>
/// JSON converter that handles backward compatibility for ISoundEffect types
/// </summary>
public sealed class SoundEffectJsonConverter : JsonConverter<ISoundEffect>
{
    public override ISoundEffect? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            if (LocalSerializationErrorNotifier.Current is not { } notifier)
            {
                notifier = NullSerializationErrorNotifier.Instance;
            }
            ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

            // Try to get the type discriminator
            if (jsonObject.TryGetDiscriminator(out string? typeString) && !string.IsNullOrEmpty(typeString))
            {
                Type? actualType = TypeFormat.ToType(typeString);
                
                // Try to deserialize as ISoundEffect type
                if (actualType?.IsAssignableTo(typeof(ISoundEffect)) == true &&
                    Activator.CreateInstance(actualType) is ISoundEffect soundEffect)
                {
                    var context = new JsonSerializationContext(actualType, notifier, parent, jsonObject);
                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        if (soundEffect is ICoreSerializable serializable)
                        {
                            serializable.Deserialize(context);
                            context.AfterDeserialized(serializable);
                        }
                    }
                    return soundEffect;
                }
            }
        }

        throw new JsonException($"Failed to deserialize sound effect from JSON");
    }

    public override void Write(Utf8JsonWriter writer, ISoundEffect value, JsonSerializerOptions options)
    {
        if (LocalSerializationErrorNotifier.Current is not { } notifier)
        {
            notifier = NullSerializationErrorNotifier.Instance;
        }

        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;
        Type valueType = value.GetType();
        var context = new JsonSerializationContext(valueType, notifier, parent);
        
        using (ThreadLocalSerializationContext.Enter(context))
        {
            if (value is ICoreSerializable serializable)
            {
                serializable.Serialize(context);
            }
        }

        JsonObject obj = context.GetJsonObject();
        obj.WriteDiscriminator(valueType);
        obj.WriteTo(writer, options);
    }
}