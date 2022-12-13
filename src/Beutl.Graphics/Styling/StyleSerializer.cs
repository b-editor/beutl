using System.Text.Json.Nodes;

using Beutl.Animation;

namespace Beutl.Styling;

public static class StyleSerializer
{
    public static JsonNode ToJson(this IStyle style)
    {
        var styleJson = new JsonObject
        {
            ["target"] = TypeFormat.ToString(style.TargetType)
        };

        var setters = new JsonObject();
        foreach (ISetter? item in style.Setters)
        {
            (string name, JsonNode? node) = item.ToJson(style.TargetType);
            setters[name] = node;
        }
        styleJson["setters"] = setters;

        return styleJson;
    }

    public static (string, JsonNode?) ToJson(this ISetter setter, Type targetType)
    {
        string? owner = null;
        JsonArray? animations = null;
        CorePropertyMetadata? metadata = setter.Property.GetMetadata<CorePropertyMetadata>(targetType);
        string? name = metadata.SerializeName ?? setter.Property.Name;

        if (!targetType.IsAssignableTo(setter.Property.OwnerType))
        {
            owner = TypeFormat.ToString(setter.Property.OwnerType);
        }

        JsonNode? value = setter.Property.RouteWriteToJson(metadata, setter.Value, out bool isDefault);

        if (setter.Animation is { } animation
            && animation.Children.Count > 0)
        {
            if (animation.Property != setter.Property)
            {
                throw new InvalidOperationException("Setter.Animation.Property != Setter.Property");
            }

            animations = new JsonArray();

            foreach (IAnimationSpan item in animation.Children)
            {
                JsonNode anmNode = new JsonObject();
                item.WriteToJson(ref anmNode);
                animations.Add(anmNode);
            }
        }

        if (value is JsonValue jsonValue
            && owner == null
            && animations == null)
        {
            return (name, jsonValue);
        }
        else if (value == null && owner == null && animations == null)
        {
            return (name, null);
        }
        else
        {
            var json = new JsonObject();
            if (value != null)
                json["value"] = value;
            if (owner != null)
                json["owner"] = owner;
            if (animations != null)
                json["animations"] = animations;

            return (name, json);
        }
    }

    public static Style? ToStyle(this JsonObject json)
    {
        if (json.TryGetPropertyValue("target", out JsonNode? targetNode)
            && targetNode is JsonValue targetValue
            && targetValue.TryGetValue(out string? targetStr)
            && TypeFormat.ToType(targetStr) is Type targetType)
        {
            var style = new Style
            {
                TargetType = targetType
            };

            if (json.TryGetPropertyValue("setters", out JsonNode? settersNode)
                && settersNode is JsonObject settersObj)
            {
                foreach (KeyValuePair<string, JsonNode?> item in settersObj)
                {
                    if (item.Value != null && item.Value.ToSetter(item.Key, targetType) is ISetter setter)
                    {
                        style.Setters.Add(setter);
                    }
                }
            }

            return style;
        }

        return null;
    }

    public static ISetter? ToSetter(this JsonNode json, string name, Type targetType)
    {
        JsonArray? animationsNode = null;
        JsonNode? valueNode = null;
        Type ownerType = targetType;

        if (json is JsonValue jsonValue)
        {
            animationsNode = null;
            valueNode = jsonValue;
        }
        else if (json is JsonObject jobj)
        {
            if (jobj.TryGetPropertyValue("owner", out JsonNode? ownerNode)
                && ownerNode is JsonValue ownerValue
                && ownerValue.TryGetValue(out string? ownerStr))
            {
                if (TypeFormat.ToType(ownerStr) is Type ownerType1)
                {
                    ownerType = ownerType1;
                }
                else
                {
                    return null;
                }
            }

            valueNode = jobj["value"];

            animationsNode = jobj["animations"] as JsonArray;
        }

        CoreProperty? property
            = PropertyRegistry.GetRegistered(ownerType).FirstOrDefault(
                x => x.GetMetadata<CorePropertyMetadata>(ownerType).SerializeName == name || x.Name == name);

        if (property == null)
            return null;

        var helper = (IGenericHelper)typeof(GenericHelper<>)
            .MakeGenericType(property.PropertyType)
            .GetField("Instance")!
            .GetValue(null)!;

        object? value = null;
        if (valueNode != null)
        {
            value = DeserializeValue(property, valueNode!, property.GetMetadata<CorePropertyMetadata>(ownerType));
        }

        var animations = new List<AnimationSpan>();
        if (animationsNode != null)
        {
            animations.EnsureCapacity(animationsNode.Count);

            foreach (JsonNode? item in animationsNode)
            {
                if (item is JsonObject animationObj)
                {
                    animations.Add(helper.DeserializeAnimation(animationObj));
                }
            }
        }

        return helper.InitializeSetter(property, value, animations);
    }

    public static object? DeserializeValue(CoreProperty property, JsonNode jsonNode, CorePropertyMetadata metadata)
    {
        return property.RouteReadFromJson(metadata, jsonNode);
    }

    private interface IGenericHelper
    {
        ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<AnimationSpan> animations);

        AnimationSpan DeserializeAnimation(JsonObject json);
    }

    private sealed class GenericHelper<T> : IGenericHelper
    {
        public static readonly GenericHelper<T> Instance = new();

        public ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<AnimationSpan> animations)
        {
            var setter = new Setter<T>((CoreProperty<T>)property);
            if (value is T t)
            {
                setter.Value = t;
            }

            var animation = new Animation<T>(setter.Property);
            animation.Children.AddRange(animations.OfType<AnimationSpan<T>>());

            setter.Animation = animation;
            return setter;
        }

        public AnimationSpan DeserializeAnimation(JsonObject json)
        {
            var anm = new AnimationSpan<T>();
            anm.ReadFromJson(json);
            return anm;
        }
    }
}
