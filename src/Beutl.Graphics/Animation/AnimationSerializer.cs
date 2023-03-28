using System.Text.Json.Nodes;

namespace Beutl.Animation;

internal static class AnimationSerializer
{
    public static (string?, JsonNode?) ToJson(this IAnimation animation, Type targetType)
    {
        JsonNode? node = animation.ToJson();
        string name = animation.Property.Name;
        if (node != null)
        {
            return (name, node);
        }
        else
        {
            return default;
        }
    }

    public static JsonNode? ToJson(this IAnimation animation)
    {
        JsonNode node = new JsonObject();
        if (animation is IKeyFrameAnimation keyFrameAnimation)
        {
            if (keyFrameAnimation.KeyFrames.Count > 0)
            {
                keyFrameAnimation.WriteToJson(ref node);
                node.WriteDiscriminator(animation.GetType());
                return node;
            }
            else
            {
                return default;
            }
        }
        else
        {
            animation.WriteToJson(ref node);
            node.WriteDiscriminator(animation.GetType());
            return node;
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
                animation.ReadFromJson(json);
                return animation;
            }
        }

        return null;
    }

}
