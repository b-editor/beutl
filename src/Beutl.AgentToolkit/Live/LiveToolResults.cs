using Beutl.AgentToolkit.Common;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Live;

// Tool results shared by the in-process instance router and the stdio broker. The payload is the
// same ToolResult<T> shape toolkit tools return, so clients parse every outcome the same way.
public static class LiveToolResults
{
    public static CallToolResult Success<T>(T value) => ToolResult<T>.Success(value).ToCallToolResult();

    public static CallToolResult Error(string code, string message, string? target = null, string? hint = null)
        => ToolResult<object?>.Failure(code, message, target, hint).ToCallToolResult();

    public static CallToolResult Error(ToolError error)
        => new ToolResult<object?>(null, error).ToCallToolResult();

    public static CallToolResult InstanceUnavailable(string instanceId)
        => Error(ErrorCode.InstanceUnavailable, "The requested Beutl instance is unavailable.", instanceId,
            "Call list_instances to find a running instance. If a previous edit lost its response, read back the target state before retrying.");

    public static CallToolResult InvalidInstanceId()
        => Error(ErrorCode.ValidationRejected, "Pass an instanceId returned by list_instances.",
            "instanceId", "Call list_instances without arguments to discover running Beutl instances.");
}
