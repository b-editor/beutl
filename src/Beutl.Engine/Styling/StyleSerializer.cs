using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl.Styling;

public static class StyleSerializer
{
    public static ISetter? ToSetter(this JsonNode? json, string name, Type targetType, ICoreSerializationContext context)
    {
        JsonNode? animationNode = null;
        JsonNode? valueNode = null;
        Type ownerType = targetType;

        if (json is JsonValue jsonValue)
        {
            animationNode = null;
            valueNode = jsonValue;
        }
        else if (json is JsonObject jobj)
        {
            if (jobj.TryGetPropertyValue("Owner", out JsonNode? ownerNode)
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

            valueNode = jobj["Value"];
            // あとで他のJsonNodeに入れるため
            jobj["Value"] = null;

            animationNode = jobj["Animation"];
        }

        CoreProperty? property = PropertyRegistry.GetRegistered(ownerType).FirstOrDefault(x => x.Name == name);

        if (property == null)
            return null;

        var helper = (IGenericHelper)typeof(GenericHelper<>)
            .MakeGenericType(property.PropertyType)
            .GetField("Instance")!
            .GetValue(null)!;

        Optional<object?> value = null;
        if (valueNode != null)
        {
            // Todo: 互換性維持のために汚くなってる
            var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, property.Name);
            var simJson = new JsonObject
            {
                [property.Name] = valueNode
            };
            var innerContext = new JsonSerializationContext(ownerType, errorNotifier, context, simJson);
            using (ThreadLocalSerializationContext.Enter(innerContext))
            {
                value = property.RouteDeserialize(innerContext);
            }
        }

        return helper.InitializeSetter(property, value, animationNode?.ToAnimation(property, context));
    }

    public static (string, JsonNode?) ToJson(this ISetter setter, Type targetType, ICoreSerializationContext context)
    {
        string? owner = null;
        JsonNode? animationNode = null;
        string? name = setter.Property.Name;

        if (!targetType.IsAssignableTo(setter.Property.OwnerType))
        {
            owner = TypeFormat.ToString(setter.Property.OwnerType);
        }

        // Todo: 互換性維持のために汚くなってる
        var simJson = new JsonObject();
        var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, name);
        var innerContext = new JsonSerializationContext(targetType, errorNotifier, context, simJson);
        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            setter.Property.RouteSerialize(innerContext, setter.Value);
        }
        JsonNode? value = simJson[name];
        simJson[name] = null;

        if (setter.Animation is { } animation)
        {
            if (animation.Property != setter.Property)
            {
                throw new InvalidOperationException("Setter.Animation.Property != Setter.Property");
            }

            animationNode = animation.ToJson(innerContext);
        }

        if (value is JsonValue jsonValue
            && owner == null
            && animationNode == null)
        {
            return (name, jsonValue);
        }
        else if (value == null && owner == null && animationNode == null)
        {
            return (name, null);
        }
        else
        {
            var json = new JsonObject
            {
                ["Value"] = value
            };

            if (owner != null)
                json["Owner"] = owner;
            if (animationNode != null)
                json["Animation"] = animationNode;

            return (name, json);
        }
    }

    [ObsoleteSerializationApi]
    public static JsonNode ToJson(this IStyle style)
    {
        var styleJson = new JsonObject
        {
            ["Target"] = TypeFormat.ToString(style.TargetType)
        };

        var setters = new JsonObject();
        foreach (ISetter? item in style.Setters)
        {
            (string name, JsonNode? node) = item.ToJson(style.TargetType);
            setters[name] = node;
        }
        styleJson["Setters"] = setters;

        return styleJson;
    }

    [ObsoleteSerializationApi]
    public static (string, JsonNode?) ToJson(this ISetter setter, Type targetType)
    {
        string? owner = null;
        JsonNode? animationNode = null;
        CorePropertyMetadata? metadata = setter.Property.GetMetadata<CorePropertyMetadata>(targetType);
        string? name = setter.Property.Name;

        if (!targetType.IsAssignableTo(setter.Property.OwnerType))
        {
            owner = TypeFormat.ToString(setter.Property.OwnerType);
        }

        JsonNode? value = setter.Property.RouteWriteToJson(metadata, setter.Value);

        if (setter.Animation is { } animation)
        {
            if (animation.Property != setter.Property)
            {
                throw new InvalidOperationException("Setter.Animation.Property != Setter.Property");
            }

            animationNode = animation.ToJson();
        }

        if (value is JsonValue jsonValue
            && owner == null
            && animationNode == null)
        {
            return (name, jsonValue);
        }
        else if (value == null && owner == null && animationNode == null)
        {
            return (name, null);
        }
        else
        {
            var json = new JsonObject();
            if (value != null)
                json["Value"] = value;
            if (owner != null)
                json["Owner"] = owner;
            if (animationNode != null)
                json["Animation"] = animationNode;

            return (name, json);
        }
    }

    [ObsoleteSerializationApi]
    public static Style? ToStyle(this JsonObject json)
    {
        if (json.TryGetPropertyValue("Target", out JsonNode? targetNode)
            && targetNode is JsonValue targetValue
            && targetValue.TryGetValue(out string? targetStr)
            && TypeFormat.ToType(targetStr) is Type targetType)
        {
            var style = new Style
            {
                TargetType = targetType
            };

            if (json.TryGetPropertyValue("Setters", out JsonNode? settersNode)
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

    [ObsoleteSerializationApi]
    public static ISetter? ToSetter(this JsonNode? json, string name, Type targetType)
    {
        JsonNode? animationNode = null;
        JsonNode? valueNode = null;
        Type ownerType = targetType;

        if (json is JsonValue jsonValue)
        {
            animationNode = null;
            valueNode = jsonValue;
        }
        else if (json is JsonObject jobj)
        {
            if (jobj.TryGetPropertyValue("Owner", out JsonNode? ownerNode)
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

            valueNode = jobj["Value"];

            animationNode = jobj["Animation"];
        }

        CoreProperty? property = PropertyRegistry.GetRegistered(ownerType).FirstOrDefault(x => x.Name == name);

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

        return helper.InitializeSetter(property, value, animationNode?.ToAnimation(property));
    }

    [ObsoleteSerializationApi]
    public static object? DeserializeValue(CoreProperty property, JsonNode jsonNode, CorePropertyMetadata metadata)
    {
        return property.RouteReadFromJson(metadata, jsonNode);
    }

    private interface IGenericHelper
    {
        ISetter InitializeSetter(CoreProperty property, object? value, IAnimation? animation);

        ISetter InitializeSetter(CoreProperty property, Optional<object?> value, IAnimation? animation);
    }

    private sealed class GenericHelper<T> : IGenericHelper
    {
        public static readonly GenericHelper<T> Instance = new();

        public ISetter InitializeSetter(CoreProperty property, object? value, IAnimation? animation)
        {
            var setter = new Setter<T>((CoreProperty<T>)property);
            if (value is T t)
            {
                setter.Value = t;
            }

            setter.Animation = animation as IAnimation<T>;
            return setter;
        }

        public ISetter InitializeSetter(CoreProperty property, Optional<object?> value, IAnimation? animation)
        {
            var setter = new Setter<T>((CoreProperty<T>)property);
            if (value.HasValue && value.Value is T t)
            {
                setter.Value = t;
            }

            setter.Animation = animation as IAnimation<T>;
            return setter;
        }
    }
}
