using System.Text.Json.Nodes;

namespace Beutl.Testing.Headless;

public static class JsonTestHelpers
{
    // The first object in node, depth first, that match accepts, or null.
    public static JsonObject? FindObject(JsonNode node, Func<JsonObject, bool> match)
    {
        if (node is JsonObject obj)
        {
            if (match(obj))
            {
                return obj;
            }

            foreach ((string _, JsonNode? child) in obj)
            {
                if (child is not null && FindObject(child, match) is { } result)
                {
                    return result;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child is not null && FindObject(child, match) is { } result)
                {
                    return result;
                }
            }
        }

        return null;
    }
}
