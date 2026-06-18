namespace Beutl.Threading;

/// <summary>
/// Runs an asynchronous operation at most once at a time. Concurrent callers join the
/// in-flight run (<see cref="RunOrJoinAsync"/>) or skip it (<see cref="TryRunAsync"/>)
/// instead of queuing their own.
/// </summary>
/// <remarks>
/// Joiners (and anyone awaiting <see cref="InFlight"/>) only wait for the run to finish; a
/// failure thrown by the operation propagates solely on the owning caller's returned task.
/// </remarks>
public sealed class SingleFlightAsyncOperation
{
    private readonly object _gate = new();
    private Task? _inFlight;

    /// <summary>
    /// Gets a task that completes when the in-flight run finishes, or <see langword="null"/> if
    /// none is in flight. It is a completion signal, not the operation's own task, so it never
    /// faults even when the operation throws.
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
    /// Runs <paramref name="operation"/> if none is in flight; otherwise returns immediately without
    /// running or awaiting the in-flight run.
    /// </summary>
    /// <param name="operation">The operation to run at most once at a time.</param>
    /// <returns><see langword="true"/> if this caller ran the operation; <see langword="false"/> if it was skipped.</returns>
    public Task<bool> TryRunAsync(Func<Task> operation) => RunCoreAsync(operation, joinInFlight: false);

    /// <summary>
    /// Runs <paramref name="operation"/> if none is in flight; otherwise awaits the in-flight run
    /// instead of starting a new one.
    /// </summary>
    /// <param name="operation">The operation to run at most once at a time.</param>
    /// <returns><see langword="true"/> if this caller ran the operation; <see langword="false"/> if it joined an in-flight run.</returns>
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
                // Completed only via TrySetResult below, so the await never throws.
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
