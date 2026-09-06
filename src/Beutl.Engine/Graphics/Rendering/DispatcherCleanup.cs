using Beutl.Threading;

namespace Beutl.Graphics.Rendering;

/// <summary>Runs one cleanup on a <see cref="Dispatcher"/> exactly once, whether or not it shuts down first.</summary>
/// <remarks>
/// A dispatcher stops draining its queue the moment a shutdown begins, so a cleanup that is only ever
/// dispatched can be dropped without running - and whoever queued it has already let go by then. This holds
/// a <see cref="Dispatcher.ShutdownFinished"/> subscription until the cleanup actually runs and re-reads the
/// dispatcher after dispatching, so a shutdown that abandons the queued cleanup still recovers it.
/// <para>
/// It waits for <see cref="Dispatcher.HasShutdownFinished"/> rather than <c>HasShutdownStarted</c>, as
/// <see cref="GpuResourceRelease"/> does: <c>Shutdown()</c> raises
/// <see cref="Dispatcher.ShutdownStarted"/> synchronously on whichever thread called it and only clears the
/// flag the dispatcher re-reads between operations, so the owner thread can still be inside one - reading
/// the very resource this would tear down. Once the loop has exited that thread is idle, so the recovery
/// runs there and stays as serialized against renders as the dispatched route.
/// </para>
/// Both routes can consequently reach the cleanup at the same moment; a one-shot flag under a private gate
/// admits exactly one, which is what work like <c>EngineObject.Resource.Dispose</c> - neither thread-safe
/// nor idempotent - requires.
/// </remarks>
internal sealed class DispatcherCleanup
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _cleanup;
    private readonly DispatchPriority _priority;
    private readonly EventHandler _shutdownHandler;
    private readonly object _gate = new();
    private bool _requested;
    private bool _settled;

    public DispatcherCleanup(
        Dispatcher dispatcher,
        Action cleanup,
        DispatchPriority priority = DispatchPriority.Low)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(cleanup);
        _dispatcher = dispatcher;
        _cleanup = cleanup;
        _priority = priority;
        _shutdownHandler = (_, _) => OnShutdownFinished();
        _dispatcher.ShutdownFinished += _shutdownHandler;
    }

    /// <summary>Asks for the cleanup, on the dispatcher when it can still run it and inline when it cannot.</summary>
    /// <remarks>
    /// Safe to call more than once and from any thread, including the dispatcher's own: only the first call
    /// that reaches the cleanup runs it.
    /// </remarks>
    public void Request()
    {
        lock (_gate)
        {
            if (_settled)
                return;

            _requested = true;
        }

        // Both license running here without racing a render: on the dispatcher's own thread nothing else
        // is running, and a finished shutdown has left that thread idle for good.
        if (_dispatcher.CheckAccess() || _dispatcher.HasShutdownFinished)
        {
            Run();
            return;
        }

        // Queued even into a shutdown that has already started: it costs one abandoned queue entry, and
        // the alternative - running here - is the race this exists to avoid. ShutdownFinished recovers it.
        _dispatcher.Dispatch(Run, _priority);
        // A shutdown that finished before _requested was published raised the event without anything to
        // recover, so the flag is the only remaining trace of it.
        if (_dispatcher.HasShutdownFinished)
            Run();
    }

    /// <summary>Drops the cleanup: the caller has established that it must not run from here.</summary>
    public void Abandon()
    {
        lock (_gate)
        {
            if (_settled)
                return;

            _settled = true;
        }

        _dispatcher.ShutdownFinished -= _shutdownHandler;
    }

    private void OnShutdownFinished()
    {
        bool recover;
        lock (_gate)
        {
            recover = _requested && !_settled;
        }

        // A cleanup queued while the dispatcher still looked alive was abandoned by this shutdown, and
        // nothing else will run it. This runs on the dispatcher's own thread, which has just left its
        // loop, so it is no more concurrent with a render than the dispatched route would have been.
        if (recover)
            Run();
    }

    private void Run()
    {
        lock (_gate)
        {
            if (_settled)
                return;

            _settled = true;
        }

        _dispatcher.ShutdownFinished -= _shutdownHandler;
        _cleanup();
    }
}
