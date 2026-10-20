using Avalonia.Threading;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.AgentHost;

public sealed class EditViewModelLiveBinding(EditViewModel editViewModel, AgentEditFollower? follower = null)
    : ILiveSessionBinding
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
            InvokeAndFollow(action);
        }
        else
        {
            Dispatcher.UIThread.Invoke(() => InvokeAndFollow(action));
        }
    }

    public void InvokeEdit(Action action, TimeSpan sessionWait, CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            // The UI thread cannot wait for the user's gesture, which needs that thread to end.
            if (editViewModel.HistoryManager.IsEditing)
                throw new UserEditingException();

            InvokeAndFollow(action);
            return;
        }

        Dispatcher.UIThread.InvokeAsync(
                () => RunAfterEditSessionAsync(() => InvokeAndFollow(action), sessionWait, cancellationToken))
            .GetAwaiter()
            .GetResult();
    }

    // Starts the deadline on the UI thread, so time spent waiting for that thread is not counted.
    private async Task RunAfterEditSessionAsync(Action edit, TimeSpan sessionWait, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(sessionWait);
        try
        {
            await editViewModel.HistoryManager.RunAfterEditAsync(edit, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new UserEditingException();
        }
    }

    // Every agent mutation reaches the editor through this binding, so the history stacks before
    // and after the call show which transaction it committed, undid or redid.
    private TResult InvokeAndFollow<TResult>(Func<TResult> operation)
    {
        AgentEditFollower.HistoryMark? mark = follower?.Capture(editViewModel);
        TResult result = operation();
        if (mark is { } before)
            follower!.FollowHistoryChange(editViewModel, before);
        return result;
    }

    private void InvokeAndFollow(Action action)
        => InvokeAndFollow(() =>
        {
            action();
            return 0;
        });

    public async ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken = default,
        TimeSpan? sessionWait = null)
    {
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(operation);

        async ValueTask<TResult> ExecuteCoreAsync()
        {
            if (sessionWait is { } wait)
                await RunAfterEditSessionAsync(static () => { }, wait, cancellationToken);

            HistoryManager history = editViewModel.HistoryManager;
            return await editViewModel.ExecuteGuardedHistoryMutationAsync(
                () => shouldPause(history),
                () => InvokeAndFollow(() => operation(history)),
                cancellationToken);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ExecuteCoreAsync();
        }

        return await Dispatcher.UIThread.InvokeAsync(async () => await ExecuteCoreAsync());
    }
}
