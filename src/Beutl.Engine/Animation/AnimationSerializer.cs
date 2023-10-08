using System.Text.Json.Nodes;

using Beutl.Serialization;
using Beutl.Serialization.Migration;

namespace Beutl.Animation;

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

    public static IAnimation? ToAnimation(this JsonNode json, CoreProperty property, ICoreSerializationContext context)
    {
        if (json is JsonObject obj)
        {
            if (Migration_ChangeSigmaType.ShouldMigrate(property))
            {
                Migration_ChangeSigmaType.Update(json);
            }

            if (obj.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type, property) is IAnimation animation)
            {
                var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, "Animation");
                var innerContext = new JsonSerializationContext(type, errorNotifier, context, obj);

                using (ThreadLocalSerializationContext.Enter(innerContext))
                {
                    animation.Deserialize(innerContext);
                }
                return animation;
            }
        }

        return null;
    }

    [ObsoleteSerializationApi]
    public static JsonNode? ToJson(this IAnimation animation)
    {
        var json = new JsonObject();
        if (animation is IKeyFrameAnimation keyFrameAnimation)
        {
            if (keyFrameAnimation.KeyFrames.Count > 0)
            {
                keyFrameAnimation.WriteToJson(json);
                json.WriteDiscriminator(animation.GetType());
                return json;
            }
            else
            {
                return default;
            }
        }
        else
        {
            animation.WriteToJson(json);
            json.WriteDiscriminator(animation.GetType());
            return json;
        }
    }

    [ObsoleteSerializationApi]
    public static IAnimation? ToAnimation(this JsonNode json, string name, Type targetType)
    {
        CoreProperty? property = PropertyRegistry.GetRegistered(targetType).FirstOrDefault(x => x.Name == name);

        if (property == null)
            return null;

        return json.ToAnimation(property);
    }

    [ObsoleteSerializationApi]
    public static IAnimation? ToAnimation(this JsonNode json, CoreProperty property)
    {
        if (json is JsonObject obj)
        {
            if (Migration_ChangeSigmaType.ShouldMigrate(property))
            {
                Migration_ChangeSigmaType.Update(json);
            }

            if (obj.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type, property) is IAnimation animation)
            {
                animation.ReadFromJson(obj);
                return animation;
            }
        }

        return null;
    }
}
