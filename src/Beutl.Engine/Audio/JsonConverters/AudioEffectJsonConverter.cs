using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph.Effects;
using Beutl.Serialization;

namespace Beutl.Audio.JsonConverters;

/// <summary>
/// JSON converter that handles compatibility between old ISoundEffect and new IAudioEffect types
/// </summary>
public sealed class AudioEffectJsonConverter : JsonConverter<IAudioEffect>
{
    private static readonly Dictionary<string, string> s_typeMapping = new()
    {
        // Map old types to new types when possible
        ["Beutl.Audio.Effects.Delay"] = "Beutl.Audio.Graph.Effects.AudioDelayEffect",
        // Add more mappings as needed for other effect types
    };

    public override IAudioEffect? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                // Check if this is an old type that needs migration
                if (s_typeMapping.TryGetValue(typeString, out string? newTypeString))
                {
                    // Try to migrate to new type
                    Type? newType = TypeFormat.ToType(newTypeString);
                    if (newType?.IsAssignableTo(typeof(IAudioEffect)) == true &&
                        Activator.CreateInstance(newType) is IAudioEffect newInstance)
                    {
                        var context = new JsonSerializationContext(newType, notifier, parent, jsonObject);
                        using (ThreadLocalSerializationContext.Enter(context))
                        {
                            if (newInstance is ICoreSerializable serializable)
                            {
                                serializable.Deserialize(context);
                                context.AfterDeserialized(serializable);
                            }
                        }
                        return newInstance;
                    }
                }

                // Try to deserialize as new IAudioEffect type
                Type? actualType = TypeFormat.ToType(typeString);
                if (actualType?.IsAssignableTo(typeof(IAudioEffect)) == true &&
                    Activator.CreateInstance(actualType) is IAudioEffect audioEffect)
                {
                    var context = new JsonSerializationContext(actualType, notifier, parent, jsonObject);
                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        if (audioEffect is ICoreSerializable serializable)
                        {
                            serializable.Deserialize(context);
                            context.AfterDeserialized(serializable);
                        }
                    }
                    return audioEffect;
                }

                // Try to deserialize as old ISoundEffect type and wrap it
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
                    
                    // Wrap the old effect to make it compatible with IAudioEffect
                    return new SoundEffectWrapper(soundEffect);
                }
            }
        }

        throw new JsonException($"Failed to deserialize audio effect from JSON");
    }

    public override void Write(Utf8JsonWriter writer, IAudioEffect value, JsonSerializerOptions options)
    {
        if (LocalSerializationErrorNotifier.Current is not { } notifier)
        {
            notifier = NullSerializationErrorNotifier.Instance;
        }

        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

        // Handle wrapped sound effects
        if (value is SoundEffectWrapper wrapper)
        {
            // Serialize the inner ISoundEffect with its original type
            Type valueType = wrapper.InnerEffect.GetType();
            var context = new JsonSerializationContext(valueType, notifier, parent);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                if (wrapper.InnerEffect is ICoreSerializable serializable)
                {
                    serializable.Serialize(context);
                }
            }

            JsonObject obj = context.GetJsonObject();
            obj.WriteDiscriminator(valueType);
            obj.WriteTo(writer, options);
        }
        else
        {
            // Serialize new IAudioEffect normally
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
}