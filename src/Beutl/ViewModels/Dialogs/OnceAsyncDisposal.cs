namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// Runs an owner's asynchronous disposal once, however many callers ask for it, and hands every
/// caller the same task.
/// </summary>
/// <remarks>
/// The disposal starts while the gate is held, so anything else the owner guards with the same
/// gate cannot interleave with the synchronous part of its start. A failure lands in the returned
/// task instead of escaping to the caller that happened to ask first.
/// </remarks>
internal sealed class OnceAsyncDisposal(object? gate = null)
{
    private readonly object _gate = gate ?? new object();
    private Task? _task;

    public Task Run(Func<Task> disposeCore, Action? onFirstRun = null)
    {
        lock (_gate)
        {
            if (_task is not null)
                return _task;

            onFirstRun?.Invoke();
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _task = completion.Task;
            _ = CompleteAsync(disposeCore, completion);
            return completion.Task;
        }
    }

    private static async Task CompleteAsync(Func<Task> disposeCore, TaskCompletionSource completion)
    {
        try
        {
            await disposeCore();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
