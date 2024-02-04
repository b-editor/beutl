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
