using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl;

[ExcludeFromCodeCoverage]
internal static class PropertyEntrySerializer
{
    public static (Optional<object?> value, IAnimation? animation) ToTuple(JsonNode? json, Type valueType)
    {
        JsonNode? animationNode = null;
        JsonNode? valueNode = null;

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
            value = CoreSerializer.DeserializeFromJsonNode(valueNode, valueType);
        }

        IAnimation? animation = null;
        if (animationNode != null)
        {
            animation = CoreSerializer.DeserializeFromJsonNode(animationNode, typeof(IAnimation)) as IAnimation;
        }

        return (value, animation);
    }

    public static JsonNode? ToJson(object? value, IAnimation? animation, Type valueType)
    {
        JsonNode? animationNode = null;

        if (animation is not null)
        {
            animationNode = CoreSerializer.SerializeToJsonObject(animation);
        }

        JsonNode? jsonNode = value != null ? CoreSerializer.SerializeToJsonNode(value) : null;

        if (jsonNode is JsonValue jsonValue
            && animationNode == null)
        {
            return jsonValue;
        }
        else if (jsonNode == null && animationNode == null)
        {
            return null;
        }
        else
        {
            var json = new JsonObject { ["Value"] = jsonNode };

            if (animationNode != null)
                json["Animation"] = animationNode;

            return json;
        }
    }
}
