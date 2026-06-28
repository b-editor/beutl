using System.Security.Cryptography;
using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Sessions;

public sealed class AgentSessionManager
{
    private readonly string _hostCompositionSeed = CreateCompositionSeed("host");
    private ISessionSource? _currentSource;
    private string? _compositionSessionKey;
    private string? _compositionSessionSeed;

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

    public string ResolveCompositionSeed(string? seed)
    {
        if (!string.IsNullOrWhiteSpace(seed))
        {
            return seed.Trim();
        }

        IEditingSession? session = CurrentSession;
        if (session is null)
        {
            return _hostCompositionSeed;
        }

        string sessionKey = $"{session.Source}:{session.SessionId}";
        if (!StringComparer.Ordinal.Equals(_compositionSessionKey, sessionKey))
        {
            _compositionSessionKey = sessionKey;
            _compositionSessionSeed = $"session:{sessionKey}";
        }

        return _compositionSessionSeed!;
    }

    private static string CreateCompositionSeed(string scope)
    {
        return $"{scope}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
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
