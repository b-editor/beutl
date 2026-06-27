using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tools;

public abstract class ToolBase
{
    protected static ToolResult<T> Execute<T>(Func<T> action)
    {
        try
        {
            return ToolResult<T>.Success(action());
        }
        catch (ReconcileException ex)
        {
            return ToolResult<T>.Failure(ex.Error.Code, ex.Error.Message, ex.Error.Target, ex.Error.Hint);
        }
        catch (WorkspaceBoundaryException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.ResolvedPath ?? ex.RequestedPath);
        }
        catch (DestructiveIntentException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Target, "Pass confirmOverwrite or confirmDelete when the destructive operation is intentional.");
        }
        catch (SchemaVersionMismatchException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Version);
        }
        catch (ProjectConflictException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Path, "Reload the project or save to a different path.");
        }
        catch (SessionUnavailableException ex)
        {
            ToolError error = ex.ToError();
            return ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint);
        }
        catch (RenderingUnavailableException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message);
        }
        catch (CodecUnavailableException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return ToolResult<T>.Failure(ErrorCode.MediaNotFound, ex.Message, ex.FileName);
        }
        catch (Exception ex)
        {
            return ToolResult<T>.Failure("internal_error", ex.Message);
        }
    }

    protected static async ValueTask<ToolResult<T>> ExecuteAsync<T>(Func<ValueTask<T>> action)
    {
        try
        {
            return ToolResult<T>.Success(await action().ConfigureAwait(false));
        }
        catch (ReconcileException ex)
        {
            return ToolResult<T>.Failure(ex.Error.Code, ex.Error.Message, ex.Error.Target, ex.Error.Hint);
        }
        catch (WorkspaceBoundaryException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.ResolvedPath ?? ex.RequestedPath);
        }
        catch (DestructiveIntentException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Target, "Pass confirmOverwrite or confirmDelete when the destructive operation is intentional.");
        }
        catch (SchemaVersionMismatchException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Version);
        }
        catch (ProjectConflictException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message, ex.Path, "Reload the project or save to a different path.");
        }
        catch (SessionUnavailableException ex)
        {
            ToolError error = ex.ToError();
            return ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint);
        }
        catch (RenderingUnavailableException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message);
        }
        catch (CodecUnavailableException ex)
        {
            return ToolResult<T>.Failure(ex.Code, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return ToolResult<T>.Failure(ErrorCode.MediaNotFound, ex.Message, ex.FileName);
        }
        catch (Exception ex)
        {
            return ToolResult<T>.Failure("internal_error", ex.Message);
        }
    }
}
