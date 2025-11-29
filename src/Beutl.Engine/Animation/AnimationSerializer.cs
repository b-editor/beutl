using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Animation;

[ExcludeFromCodeCoverage]
internal static class AnimationSerializer
{
    public static JsonNode? ToJson(this IAnimation animation, ICoreSerializationContext context)
    {
        Type type = animation.GetType();
        var json = new JsonObject();
        var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, "Animation");
        var innerContext = new JsonSerializationContext(type, errorNotifier, context, json);

        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            animation.Serialize(innerContext);
            json.WriteDiscriminator(type);
        }
        return json;
    }

    public static IAnimation? ToAnimation(this JsonNode json, ICoreSerializationContext context)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type) is IAnimation animation)
            {
                var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, "Animation");
                var innerContext = new JsonSerializationContext(type, errorNotifier, context, obj);

                using (ThreadLocalSerializationContext.Enter(innerContext))
                {
                    animation.Deserialize(innerContext);
                    innerContext.AfterDeserialized(animation);
                }
                return animation;
            }
        }

        return null;
    }
}
