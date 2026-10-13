using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Common;

public static class IdentityHelper
{
    public static string WriteDiscriminator(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var json = new JsonObject();
        json.WriteDiscriminator(type);
        return json["$type"]!.GetValue<string>();
    }
}
