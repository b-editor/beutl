using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl;

[ExcludeFromCodeCoverage]
internal static class PropertyEntrySerializer
{
    public static void Write(ICoreSerializationContext context, string name, object? value, IKeyFrameAnimation? animation)
    {
        if (context is CoreSerializer.VisitorContext)
            context.SetValue(name, new Entry(value, animation));
        else
            context.SetValue(name, ToJson(value, animation));
    }

    private sealed class Entry(object? value, IKeyFrameAnimation? animation) : ICoreSerializable
    {
        public void Serialize(ICoreSerializationContext context)
        {
            context.SetValue("Value", value);
            context.SetValue("Animation", animation);
        }

        public void Deserialize(ICoreSerializationContext context) => throw new NotSupportedException();
    }

    public static (Optional<object?> value, IKeyFrameAnimation? animation) ToTuple(JsonNode? json, Type valueType)
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

        IKeyFrameAnimation? animation = null;
        if (animationNode != null)
        {
            animation = CoreSerializer.DeserializeFromJsonNode(animationNode, typeof(IKeyFrameAnimation)) as IKeyFrameAnimation;
        }

        return (value, animation);
    }

    public static JsonNode? ToJson(object? value, IKeyFrameAnimation? animation)
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
