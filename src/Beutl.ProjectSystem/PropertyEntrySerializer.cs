using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl;

[ExcludeFromCodeCoverage]
internal static class PropertyEntrySerializer
{
    public static (Optional<object?> value, IAnimation? animation) ToTuple(JsonNode? json, string name, Type valueType, Type targetType, ICoreSerializationContext context)
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
            valueNode = jobj["Value"];
            // あとで他のJsonNodeに入れるため
            jobj["Value"] = null;

            animationNode = jobj["Animation"];
        }

        Optional<object?> value = null;
        if (valueNode != null)
        {
            // Todo: 互換性維持のために汚くなってる
            var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, name);
            var simJson = new JsonObject
            {
                [name] = valueNode
            };
            var innerContext = new JsonSerializationContext(ownerType, errorNotifier, context, simJson);
            using (ThreadLocalSerializationContext.Enter(innerContext))
            {
                value = innerContext.GetValue(name, valueType);
            }
        }

        return (value, animationNode?.ToAnimation(context));
    }

    public static (string, JsonNode?) ToJson(string name, object? value, IAnimation? animation, Type valueType, Type targetType, ICoreSerializationContext context)
    {
        string? owner = null;
        JsonNode? animationNode = null;

        // Todo: 互換性維持のために汚くなってる
        var simJson = new JsonObject();
        var errorNotifier = new RelaySerializationErrorNotifier(context.ErrorNotifier, name);
        var innerContext = new JsonSerializationContext(targetType, errorNotifier, context, simJson);
        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            innerContext.SetValue(name, value, valueType);
        }
        JsonNode? jsonNode = simJson[name];
        simJson[name] = null;

        if (animation is not null)
        {
            // if (animation.Property.Id != property.Id)
            // {
            //     throw new InvalidOperationException("Animation.Property != Property");
            // }

            animationNode = animation.ToJson(innerContext);
        }

        if (jsonNode is JsonValue jsonValue
            && owner == null
            && animationNode == null)
        {
            return (name, jsonValue);
        }
        else if (jsonNode == null && owner == null && animationNode == null)
        {
            return (name, null);
        }
        else
        {
            var json = new JsonObject
            {
                ["Value"] = jsonNode
            };

            if (owner != null)
                json["Owner"] = owner;
            if (animationNode != null)
                json["Animation"] = animationNode;

            return (name, json);
        }
    }
}
