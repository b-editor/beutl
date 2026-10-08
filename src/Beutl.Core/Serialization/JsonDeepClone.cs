using System.Text.Json.Nodes;

namespace Beutl.Serialization;

internal static class JsonDeepClone
{
    public static void CopyTo(JsonObject source, JsonObject destination)
    {
        foreach (KeyValuePair<string, JsonNode?> item in source)
        {
            destination[item.Key] = item.Value?.DeepClone();
        }
    }
}
