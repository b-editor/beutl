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
            "Open a project in the stdio host or attach the active editor in the in-app host.");
    }
}
