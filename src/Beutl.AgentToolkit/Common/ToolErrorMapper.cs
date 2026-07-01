using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Common;

internal static class ToolErrorMapper
{
    public static ToolError Map(Exception exception)
    {
        return exception switch
        {
            ReconcileException ex => ex.Error,
            WorkspaceBoundaryException ex => new ToolError(ex.Code, ex.Message, ex.ResolvedPath ?? ex.RequestedPath),
            DestructiveIntentException ex => new ToolError(
                ex.Code,
                ex.Message,
                ex.Target,
                "Pass confirmOverwrite or confirmDelete when the destructive operation is intentional."),
            SchemaVersionMismatchException ex => new ToolError(ex.Code, ex.Message, ex.Version),
            ProjectConflictException ex => new ToolError(
                ex.Code,
                ex.Message,
                ex.Path,
                "Reload the project or save to a different path."),
            SessionUnavailableException ex => ex.ToError(),
            RenderingUnavailableException ex => new ToolError(ex.Code, ex.Message),
            CodecUnavailableException ex => new ToolError(ex.Code, ex.Message),
            FileNotFoundException ex => new ToolError(ErrorCode.MediaNotFound, ex.Message, ex.FileName),
            _ => new ToolError("internal_error", exception.Message)
        };
    }
}
