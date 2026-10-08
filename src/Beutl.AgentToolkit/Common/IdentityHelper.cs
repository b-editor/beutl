using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Common;

public static class IdentityHelper
{
    public static ICoreObject? FindById(ICoreObject root, Guid id, bool includeSelf = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.FindById(id, includeSelf);
    }

    public static string WriteDiscriminator(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var json = new JsonObject();
        json.WriteDiscriminator(type);
        return json["$type"]!.GetValue<string>();
    }
}
