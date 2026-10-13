using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Live;

// Argument and schema rewriting shared by the stdio broker and the editor's routers. The argument
// descriptions differ per host, so callers pass them in.
public static class LiveToolRouting
{
    // An instanceId is a GUID in "N" form, and list_instances never takes one.
    public static bool TryGetInstanceId(string toolName, JsonElement id, [NotNullWhen(true)] out string? instanceId)
    {
        instanceId = toolName != LiveMcpBroker.ListInstancesToolName
                     && id.ValueKind == JsonValueKind.String
                     && Guid.TryParseExact(id.GetString(), "N", out _)
            ? id.GetString()
            : null;
        return instanceId is not null;
    }

    public static CallToolRequestParams WithArguments(
        CallToolRequestParams request, IDictionary<string, JsonElement> arguments)
        => new()
        {
            Name = request.Name,
            Arguments = arguments,
            Meta = request.Meta,
            InputResponses = request.InputResponses,
            RequestState = request.RequestState
        };

    public static CallToolRequestParams WithoutArgument(
        CallToolRequestParams request, IDictionary<string, JsonElement> arguments, string name)
    {
        var forwarded = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
        forwarded.Remove(name);
        return WithArguments(request, forwarded);
    }

    // Edits the input schema of a clone of the tool, passing the schema and its properties object,
    // which is created when missing.
    public static Tool EditSchema(Tool tool, Action<JsonObject, JsonObject> edit)
    {
        JsonObject node = JsonSerializer.SerializeToNode(tool)!.AsObject();
        JsonObject schema = node["inputSchema"]!.AsObject();
        var properties = schema["properties"] as JsonObject;
        if (properties is null)
            schema["properties"] = properties = new JsonObject();
        edit(schema, properties);
        return node.Deserialize<Tool>()!;
    }

    public static void RemoveRequired(JsonObject schema, string name)
    {
        if (schema["required"] is JsonArray required)
        {
            JsonNode? requirement = required.FirstOrDefault(item => item?.GetValue<string>() == name);
            if (requirement is not null)
                required.Remove(requirement);
        }
    }

    public static AgentHostInstanceInfo[] OrderByProcess(IEnumerable<AgentHostInstanceInfo?> infos)
        => infos.OfType<AgentHostInstanceInfo>()
            .OrderBy(info => info.ProcessId).ThenBy(info => info.InstanceId, StringComparer.Ordinal).ToArray();
}
