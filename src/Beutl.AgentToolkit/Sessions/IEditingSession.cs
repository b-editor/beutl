using Beutl.AgentToolkit.Documents;
using Beutl.Editor;

namespace Beutl.AgentToolkit.Sessions;

public enum EditingSessionSource
{
    File,
    LiveEditor,
}

public interface IEditingSession
{
    string SessionId { get; }

    EditingSessionSource Source { get; }

    CoreObject Root { get; }

    HistoryManager History { get; }

    DocumentAdapter Documents { get; }

    bool IsDirty { get; }

    // The fonts this session's project renders with, captured with a scene snapshot so a render or export keeps
    // them after the session is replaced; null resolves against the host's fonts.
    Media.FontManager? Fonts => null;

    void Invoke(Action action);

    // For edits: a live session runs them after the user's edit session, waiting up to a deadline.
    void InvokeEdit(Action action, CancellationToken cancellationToken = default) => Invoke(action);
}
