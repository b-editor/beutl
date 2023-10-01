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

    private static Type? TryGetKeyFrameValueType(Type animationType)
    {
        Type[] interfaces = animationType.GetInterfaces();
        if (interfaces.FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IAnimation<>))
             is { } interfaceType)
        {
            return interfaceType.GetGenericArguments()[0];
        }

        return null;
    }

    public override IKeyFrame Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            if (LocalSerializationErrorNotifier.Current is not { } notifier)
            {
                notifier = NullSerializationErrorNotifier.Instance;
            }

            ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

            Type? actualType = null;
            if (parent != null)
            {
                Type parentType = parent.OwnerType;
                Type? keyFrameValueType = TryGetKeyFrameValueType(parentType);
                if (keyFrameValueType != null)
                {
                    actualType = typeof(KeyFrame<>).MakeGenericType(keyFrameValueType);
                }
            }
            actualType ??= typeToConvert.IsSealed ? typeToConvert : jsonObject.GetDiscriminator(typeToConvert);

            if (actualType?.IsAssignableTo(typeToConvert) == true
                && Activator.CreateInstance(actualType) is IKeyFrame instance)
            {
                var context = new JsonSerializationContext(actualType, notifier, parent, jsonObject);
                using (ThreadLocalSerializationContext.Enter(context))
                {
                    instance.Deserialize(context);
                }

                return instance;
            }
        }

        throw new Exception("Invalid IKeyFrame");
    }

    public override void Write(Utf8JsonWriter writer, IKeyFrame value, JsonSerializerOptions options)
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
            value.Serialize(context);
        }

        JsonObject obj = context.GetJsonObject();

        if (!(parent?.OwnerType is { } parentType)
            || TryGetKeyFrameValueType(parentType) == null)
        {
            obj.WriteDiscriminator(valueType);
        }

        obj.WriteTo(writer, options);
    }
}
