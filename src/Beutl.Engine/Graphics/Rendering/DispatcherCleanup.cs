using Beutl.Threading;

namespace Beutl.Graphics.Rendering;

/// <summary>Runs one cleanup on a <see cref="Dispatcher"/> exactly once, whether or not it shuts down first.</summary>
/// <remarks>
/// A dispatcher stops draining its queue the moment a shutdown begins, so a cleanup that is only ever
/// dispatched can be dropped without running - and whoever queued it has already let go by then. This holds
/// a <see cref="Dispatcher.ShutdownStarted"/> subscription until the cleanup actually runs and re-reads the
/// dispatcher after dispatching, so a shutdown starting before either check is still recovered inline on the
/// thread that noticed it. Both routes can consequently reach the cleanup at the same moment; a one-shot
/// flag under a private gate admits exactly one, which is what work like <c>EngineObject.Resource.Dispose</c>
/// - neither thread-safe nor idempotent - requires.
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
        _shutdownHandler = (_, _) => OnShutdownStarted();
        _dispatcher.ShutdownStarted += _shutdownHandler;
    }

    /// <summary>Asks for the cleanup, on the dispatcher when it is still draining work and inline when not.</summary>
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

        // A shutting-down dispatcher never runs queued work, so the cleanup has to happen inline there.
        if (_dispatcher.CheckAccess() || _dispatcher.HasShutdownStarted)
        {
            Run();
            return;
        }

        _dispatcher.Dispatch(Run, _priority);
        // Shutdown can begin between the check above and the dispatch, abandoning the queued cleanup before
        // the still-registered handler is given anything to recover.
        if (_dispatcher.HasShutdownStarted)
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

        _dispatcher.ShutdownStarted -= _shutdownHandler;
    }

    private void OnShutdownStarted()
    {
        bool recover;
        lock (_gate)
        {
            recover = _requested && !_settled;
        }

        // A cleanup queued while the dispatcher still looked alive is abandoned by this shutdown, and
        // nothing else will run it.
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

        _dispatcher.ShutdownStarted -= _shutdownHandler;
        _cleanup();
    }
}
