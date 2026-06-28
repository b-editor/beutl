using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Sessions;

public sealed class AgentSessionManager
{
    private ISessionSource? _currentSource;

    public IEditingSession? CurrentSession => _currentSource?.CurrentSession;

    public void UseSource(ISessionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _currentSource = source;
    }

    public IEditingSession RequireSession()
    {
        return CurrentSession
               ?? throw new SessionUnavailableException();
    }
}

public sealed class SessionUnavailableException : Exception
{
    public SessionUnavailableException()
        : base("No active editing session is available.")
    {
    }

    public ToolError ToError()
    {
        return new ToolError(
            ErrorCode.NoActiveEditorSession,
            Message,
            null,
            "In the in-app host, call attach_active_editor before read_document_summary, read_document, plan_edit, apply_edit, render_still, or export_video. In the stdio host, call open_project or create_project first.");
    }
}
