using System.Text.Json.Nodes;

namespace Beutl.Animation;

internal static class AnimationSerializer
{
    public static (string?, JsonNode?) ToJson(this IAnimation animation, Type targetType)
    {
        JsonNode? node = animation.ToJson();
        string name = animation.Property.GetMetadata<CorePropertyMetadata>(targetType).SerializeName ?? animation.Property.Name;
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
                node["@type"] = TypeFormat.ToString(animation.GetType());
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
            node["@type"] = TypeFormat.ToString(animation.GetType());
            return node;
        }
    }

    public static IAnimation? ToAnimation(this JsonNode json, string name, Type targetType)
    {
        CoreProperty? property
            = PropertyRegistry.GetRegistered(targetType).FirstOrDefault(
                x => x.GetMetadata<CorePropertyMetadata>(targetType).SerializeName == name || x.Name == name);

        if (property == null)
            return null;

        return json.ToAnimation(property);
    }

    public static IAnimation? ToAnimation(this JsonNode json, CoreProperty property)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                && atTypeNode is JsonValue atTypeValue
                && atTypeValue.TryGetValue(out string? atType)
                && atType != null
                && TypeFormat.ToType(atType) is Type type
                && Activator.CreateInstance(type, property) is IAnimation animation)
            {
                animation.ReadFromJson(json);
                return animation;
            }
        }

        return null;
    }

}
