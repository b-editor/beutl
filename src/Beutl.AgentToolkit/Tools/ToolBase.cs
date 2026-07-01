using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Tools;

public abstract class ToolBase
{
    protected static ToolResult<T> Execute<T>(Func<T> action)
    {
        try
        {
            return ToolResult<T>.Success(action());
        }
        catch (Exception ex)
        {
            ToolError error = ToolErrorMapper.Map(ex);
            return ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint);
        }
    }

    protected static async ValueTask<ToolResult<T>> ExecuteAsync<T>(Func<ValueTask<T>> action)
    {
        try
        {
            return ToolResult<T>.Success(await action().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            ToolError error = ToolErrorMapper.Map(ex);
            return ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint);
        }
    }
}
