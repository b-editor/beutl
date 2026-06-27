using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public interface ILiveSessionBinding
{
    Scene? ActiveScene { get; }

    HistoryManager? ActiveHistory { get; }

    bool IsAlive { get; }

    void Invoke(Action action);
}
