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

    public bool IsAlive => editViewModel.Scene is not null;

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
