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

    // Host-controlled transitions disable the editor before closing or replacing its scene. An
    // accepted mutation in that window would only reach stale in-memory state, so the session is
    // unavailable for exactly as long as the editor is disabled.
    public bool IsAlive
    {
        get
        {
            if (editViewModel.Scene is null)
            {
                return false;
            }

            try
            {
                return editViewModel.IsEnabled.Value
                       && !editViewModel.IsDisposingOrDisposed;
            }
            catch (ObjectDisposedException)
            {
                // Disposal tears down reactive properties before clearing Scene. Treat that short
                // interval as unavailable instead of leaking the implementation exception.
                return false;
            }
        }
    }

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
