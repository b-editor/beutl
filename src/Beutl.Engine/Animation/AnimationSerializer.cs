using System.Text.Json.Nodes;

namespace Beutl.Animation;

internal static class AnimationSerializer
{
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

    public static IAnimation? ToAnimation(this JsonNode json, string name, Type targetType)
    {
        CoreProperty? property = PropertyRegistry.GetRegistered(targetType).FirstOrDefault(x => x.Name == name);

        if (property == null)
            return null;

        return json.ToAnimation(property);
    }

    public static IAnimation? ToAnimation(this JsonNode json, CoreProperty property)
    {
        if (json is JsonObject obj)
        {
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
