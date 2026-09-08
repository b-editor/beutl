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

    public async ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(operation);

        async ValueTask<TResult> ExecuteCoreAsync()
        {
            HistoryManager history = editViewModel.HistoryManager;
            return await editViewModel.ExecuteGuardedHistoryMutationAsync(
                () => shouldPause(history),
                () => operation(history),
                cancellationToken);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ExecuteCoreAsync();
        }

        return await Dispatcher.UIThread.InvokeAsync(async () => await ExecuteCoreAsync());
    }
}
