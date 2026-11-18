using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;

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
            return new ToolResult<T>(default, ToolErrorMapper.Map(ex));
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
            return new ToolResult<T>(default, ToolErrorMapper.Map(ex));
        }
    }

    protected static Scene RequireSceneRoot(IEditingSession session)
    {
        if (session.Root is not Scene scene)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Current root '{session.Root.GetType().FullName}' is not a Scene.",
                session.Root.Id.Value.ToString()));
        }

        return scene;
    }
}
