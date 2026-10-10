using System.Text.Json;
using Beutl.AgentToolkit.Common;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Live;

// Tool results shared by the in-process instance router and the stdio broker. The payload is the
// same ToolResult<T> shape toolkit tools return, so clients parse every outcome the same way.
public static class LiveToolResults
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static CallToolResult Success<T>(T value) => Text(ToolResult<T>.Success(value));

    public static CallToolResult Error(string code, string message, string? target = null, string? hint = null)
        => Text(ToolResult<object?>.Failure(code, message, target, hint));

    public static CallToolResult Error(ToolError error)
        => Text(new ToolResult<object?>(null, error));

    public static CallToolResult InstanceUnavailable(string instanceId)
        => Error(ErrorCode.InstanceUnavailable, "The requested Beutl instance is unavailable.", instanceId,
            "Call list_instances to find a running instance. If a previous edit lost its response, read back the target state before retrying.");

    public static CallToolResult InvalidInstanceId()
        => Error(ErrorCode.ValidationRejected, "Pass an instanceId returned by list_instances.",
            "instanceId", "Call list_instances without arguments to discover running Beutl instances.");

    private static CallToolResult Text<T>(ToolResult<T> result)
        => new()
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, s_jsonOptions) }]
        };
}
