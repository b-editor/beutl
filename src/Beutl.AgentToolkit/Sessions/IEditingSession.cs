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

    void Invoke(Action action);

    // For edits: a live session runs them after the user's edit session, waiting up to a deadline.
    void InvokeEdit(Action action, CancellationToken cancellationToken = default) => Invoke(action);
}
