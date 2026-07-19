using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Reconciliation;

internal static class ValidationValueNode
{
    // System.Text.Json cannot write a live engine object: IProperty.ValueType is a System.Type, and
    // IFileSource needs a serialization context that is gone by the time the response is written.
    public static JsonNode? From(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return node.DeepClone();
            case string text:
                return JsonValue.Create(text);
        }

        try
        {
            return CoreSerializer.SerializeToJsonNode(value);
        }
        catch (Exception)
        {
            return JsonValue.Create(value.ToString());
        }
    }
}
