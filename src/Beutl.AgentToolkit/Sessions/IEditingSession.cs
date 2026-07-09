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
}
