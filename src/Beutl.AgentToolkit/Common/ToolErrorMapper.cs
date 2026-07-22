using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;

namespace Beutl.AgentToolkit.Common;

internal static class ToolErrorMapper
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ToolErrorMapper));

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
            UnsupportedMediaException ex => new ToolError(ErrorCode.MediaUnsupported, ex.Message, ex.FileName),
            FileNotFoundException ex => new ToolError(ErrorCode.MediaNotFound, ex.Message, ex.FileName),
            _ => MapUnexpected(exception)
        };
    }

    private static ToolError MapUnexpected(Exception exception)
    {
        // exception.Message can embed absolute filesystem paths; keep it server-side and expose
        // only path-free structured facts: the type, the throw site, and any parameter name.
        s_logger.LogError(exception, "Unmapped tool exception.");
        return new ToolError(
            "internal_error",
            $"An internal error occurred ({DescribeUnexpected(exception)}).",
            null,
            "The full exception was logged to the MCP server log (stderr).");
    }

    private static string DescribeUnexpected(Exception exception)
    {
        string detail = exception.GetType().Name;
        if (exception.TargetSite is { } site && site.DeclaringType is { } declaringType)
        {
            detail += $" in {declaringType.Name}.{site.Name}";
        }

        // Only short identifier-shaped names are exposed: ParamName is free text in principle,
        // and the redaction contract above must hold even for a pathological path-carrying value.
        if (exception is ArgumentException { ParamName: { Length: > 0 and <= MaxParamNameLength } paramName }
            && IsIdentifierLike(paramName))
        {
            detail += $", parameter '{paramName}'";
        }

        return detail;
    }

    private const int MaxParamNameLength = 128;

    private static bool IsIdentifierLike(string name)
    {
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
