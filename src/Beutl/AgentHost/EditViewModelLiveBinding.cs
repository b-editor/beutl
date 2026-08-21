using Avalonia.Threading;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.AgentHost;

public sealed class EditViewModelLiveBinding(EditViewModel editViewModel) : ILiveSessionBinding
{
    public Scene? ActiveScene => editViewModel.Scene;

    public HistoryManager? ActiveHistory => editViewModel.HistoryManager;

    // A version-control transition suspends the editors from before its pre-transition save until
    // the project closes. An edit accepted in that window would only reach the in-memory scene and
    // be discarded by the close, so the session counts as unavailable for exactly as long as the
    // editor is disabled.
    public bool IsAlive => editViewModel.Scene is not null && editViewModel.IsEnabled.Value;

    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }
}
