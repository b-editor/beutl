namespace Beutl.AgentToolkit.Common;

public sealed record ToolError(
    string Code,
    string Message,
    string? Target = null,
    string? Hint = null);

public sealed record ToolResult<T>(T? Value, ToolError? Error)
{
    public bool IsSuccess => Error is null;

    public static ToolResult<T> Success(T value)
    {
        return new ToolResult<T>(value, null);
    }

    public static ToolResult<T> Failure(string code, string message, string? target = null, string? hint = null)
    {
        return new ToolResult<T>(default, new ToolError(code, message, target, hint));
    }
}
