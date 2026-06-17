namespace Beutl.Threading;

/// <summary>
/// Runs an asynchronous operation at most once at a time. Concurrent callers join the
/// in-flight run (<see cref="RunOrJoinAsync"/>) or skip it (<see cref="TryRunAsync"/>)
/// rather than queuing their own; the run is exposed as a <see cref="Task"/> so joiners
/// can await it instead of busy-waiting on a shared flag.
/// </summary>
/// <remarks>
/// Unlike a classic single-flight cache, joiners (and anyone awaiting <see cref="InFlight"/>)
/// wait only for the run to finish; they do not observe its result or exception. A failure
/// thrown by the operation propagates solely on the owning caller's returned task.
/// </remarks>
public sealed class SingleFlightAsyncOperation
{
    private readonly object _gate = new();
    private Task? _inFlight;

    /// <summary>
    /// Gets a task that completes when the in-flight run finishes, or <see langword="null"/> if
    /// none is in flight. This is a completion signal, not the operation's own task: it always
    /// completes successfully even when the operation throws, so awaiting it never observes the
    /// owner's failure.
    /// </summary>
    public Task? InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> if none is in flight; if one is already in flight, returns
    /// immediately without running or awaiting it.
    /// </summary>
    /// <param name="operation">The operation to run at most once at a time.</param>
    /// <returns>
    /// <see langword="true"/> if this caller ran <paramref name="operation"/>; <see langword="false"/>
    /// if it was skipped because another run was already in flight. <see langword="true"/> means the
    /// operation was executed, not that it succeeded — a failure it throws propagates on the returned task.
    /// </returns>
    public Task<bool> TryRunAsync(Func<Task> operation) => RunCoreAsync(operation, joinInFlight: false);

    /// <summary>
    /// Runs <paramref name="operation"/> if none is in flight; if one is already in flight, awaits its
    /// completion instead of starting a new run.
    /// </summary>
    /// <param name="operation">The operation to run at most once at a time.</param>
    /// <returns>
    /// <see langword="true"/> if this caller ran <paramref name="operation"/>; <see langword="false"/>
    /// if it joined an in-flight run. A joiner waits for the in-flight run to finish but does not observe
    /// its result or exception; only the owning caller's returned task propagates a failure it throws.
    /// </returns>
    public Task<bool> RunOrJoinAsync(Func<Task> operation) => RunCoreAsync(operation, joinInFlight: true);

    private async Task<bool> RunCoreAsync(Func<Task> operation, bool joinInFlight)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Task? existing;
        TaskCompletionSource? owned = null;
        lock (_gate)
        {
            existing = _inFlight;
            if (existing is null)
            {
                owned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight = owned.Task;
            }
        }

        if (existing is not null)
        {
            if (joinInFlight)
            {
                // existing is the in-flight TaskCompletionSource task, completed only via
                // TrySetResult below, so awaiting it cannot throw. Joiners wait for the run
                // to finish without observing the owner's result or exception.
                await existing.ConfigureAwait(false);
            }

            return false;
        }

        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }

            // Signal completion so joiners resume regardless of the owner's outcome.
            owned!.TrySetResult();
        }

        return true;
    }
}
