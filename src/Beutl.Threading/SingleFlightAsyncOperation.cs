namespace Beutl.Threading;

/// <summary>
/// Runs an asynchronous operation at most once at a time. Concurrent callers join the
/// in-flight run (<see cref="RunOrJoinAsync"/>) or skip it (<see cref="TryRunAsync"/>)
/// rather than queuing their own; the run is exposed as a <see cref="Task"/> so joiners
/// can await it instead of busy-waiting on a shared flag.
/// </summary>
public sealed class SingleFlightAsyncOperation
{
    private readonly object _gate = new();
    private Task? _inFlight;

    /// <summary>
    /// Gets the currently running operation, or <see langword="null"/> if none is in flight.
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
    /// Runs <paramref name="operation"/> if none is in flight and returns <see langword="true"/>.
    /// If one is already in flight, returns <see langword="false"/> immediately without awaiting it.
    /// </summary>
    public Task<bool> TryRunAsync(Func<Task> operation) => RunCoreAsync(operation, joinInFlight: false);

    /// <summary>
    /// Runs <paramref name="operation"/> if none is in flight and returns <see langword="true"/>.
    /// If one is already in flight, awaits its completion and returns <see langword="false"/>.
    /// </summary>
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
                // Joiners only wait for the run to finish; the owner observes any failure.
                try { await existing.ConfigureAwait(false); }
                catch { }
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
