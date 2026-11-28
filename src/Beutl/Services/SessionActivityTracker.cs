using Avalonia.Threading;
using Reactive.Bindings;

namespace Beutl.Services;

// What keeps a session busy. A kind names the source of a lease, for logs and tests; every kind keeps the session
// alive the same way.
internal enum SessionActivityKind
{
    // An output or export reserving the project's files, or an output profile running.
    Output,
    ProjectFileWrite,
    EditorFileOpen,
    WorktreeMutation,
    // Slow work shown over the project's editors, such as a close or a version-control cycle.
    Lifecycle,
    // An auto-save from the moment an edit queues it until its files are written.
    AutoSave,
    // Work of the version control tracking the project, including a save snapshot waiting for the gate.
    VersionControl,
    // An AI generation, edit or subtitle run started from one of the project's editors.
    AiGeneration,
    // An agent thread running a turn on the project.
    AgentThread,
    // An AI job an agent started on the project.
    AgentAiJob,
    // Preview playback of one of the project's scenes, until its render loop has drained.
    Playback,
}

// The work running for one session, as leases. Each source of work takes a lease when it starts and disposes it on
// every path out, so "nothing runs for this session" is decided from the leases alone rather than by polling the
// view models. The editor workspace of a project (keyed by the editor service and the project file) owns one.
//
// Leases can be taken and disposed from any thread. Never take or dispose one while holding another lock that a
// subscriber of IsBusy could need: IsBusy is published on the UI thread, synchronously when the change happens there.
internal sealed class SessionActivityTracker
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Action? _idle;
    private readonly int[] _counts = new int[Enum.GetValues<SessionActivityKind>().Length];
    private readonly ReactivePropertySlim<bool> _isBusy = new();
    private TaskCompletionSource? _drained;
    private int _activeLeases;
    private long _lastIdleSince;
    private int _publishScheduled;

    // idle runs, outside the tracker's lock and on the thread that disposed the last lease, each time the last
    // lease ends.
    internal SessionActivityTracker(TimeProvider? timeProvider = null, Action? idle = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idle = idle;
        _lastIdleSince = _timeProvider.GetTimestamp();
        IsBusy = _isBusy.ToReadOnlyReactivePropertySlim();
    }

    // Whether any lease is held, published on the UI thread. HasActiveLeases answers at once from any thread.
    internal IReadOnlyReactiveProperty<bool> IsBusy { get; }

    internal bool HasActiveLeases
    {
        get
        {
            lock (_sync)
            {
                return _activeLeases != 0;
            }
        }
    }

    // The timestamp (TimeProvider.GetTimestamp) at which the last lease ended, or the tracker was made if none was
    // ever taken. It moves on each time the session becomes idle, so a finished job re-arms an idle timeout; while a
    // lease is held it still reads the previous moment, so a reader checks HasActiveLeases or IsBusy first.
    internal long LastIdleSince
    {
        get
        {
            lock (_sync)
            {
                return _lastIdleSince;
            }
        }
    }

    internal TimeProvider TimeProvider => _timeProvider;

    // The leases of kind held now.
    internal int CountOf(SessionActivityKind kind)
    {
        lock (_sync)
        {
            return _counts[(int)kind];
        }
    }

    // Keeps the session busy until the returned lease is disposed. Disposing it twice ends it once.
    internal IDisposable Begin(SessionActivityKind kind)
    {
        if ((uint)kind >= (uint)_counts.Length)
            throw new ArgumentOutOfRangeException(nameof(kind));

        lock (_sync)
        {
            _counts[(int)kind]++;
            _activeLeases++;
        }

        Publish();
        return new Lease(this, kind);
    }

    // Keeps the session busy while active last reported true, until active completes or fails, or the returned
    // subscription is disposed.
    internal IDisposable HoldWhile(IObservable<bool> active, SessionActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(active);
        var hold = new Hold(this, kind);
        hold.Attach(active.Subscribe(hold));
        return hold;
    }

    // Completes once no lease is held, at once if none is. A lease taken after it completed is not waited for, so a
    // caller that must stay idle afterwards takes its own lease first and waits for the others to end.
    internal Task WaitForIdleAsync(CancellationToken cancellationToken = default)
        => WaitForIdleAsync(ignoredLeases: 0, cancellationToken);

    // Completes once at most ignoredLeases leases are held, which lets a caller holding its own lease wait for the
    // others.
    internal Task WaitForIdleAsync(int ignoredLeases, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ignoredLeases);
        return WaitAsync();

        async Task WaitAsync()
        {
            while (true)
            {
                Task drained;
                lock (_sync)
                {
                    if (_activeLeases <= ignoredLeases)
                        return;

                    // Completed whenever the count goes down, so each waiter re-checks its own threshold.
                    drained = (_drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }

                await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void End(SessionActivityKind kind)
    {
        TaskCompletionSource? drained;
        bool idle;
        lock (_sync)
        {
            _counts[(int)kind]--;
            idle = --_activeLeases == 0;
            if (idle)
                _lastIdleSince = _timeProvider.GetTimestamp();
            drained = _drained;
            _drained = null;
        }

        drained?.TrySetResult();
        Publish();
        if (idle)
            _idle?.Invoke();
    }

    private void Publish()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateIsBusy();
        }
        else if (Interlocked.Exchange(ref _publishScheduled, 1) == 0)
        {
            // The posted update reads the count when it runs, so changes arriving out of order still settle on the
            // current state.
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _publishScheduled, 0);
                UpdateIsBusy();
            });
        }
    }

    private void UpdateIsBusy() => _isBusy.Value = HasActiveLeases;

    private sealed class Lease(SessionActivityTracker owner, SessionActivityKind kind) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.End(kind);
        }
    }

    private sealed class Hold(SessionActivityTracker owner, SessionActivityKind kind) : IObserver<bool>, IDisposable
    {
        private readonly object _sync = new();
        private IDisposable? _lease;
        private IDisposable? _subscription;
        private bool _stopped;

        public void Attach(IDisposable subscription)
        {
            bool stopped;
            lock (_sync)
            {
                stopped = _stopped;
                if (!stopped)
                    _subscription = subscription;
            }

            if (stopped)
                subscription.Dispose();
        }

        public void OnNext(bool value)
        {
            IDisposable? released = null;
            lock (_sync)
            {
                if (value && !_stopped)
                {
                    _lease ??= owner.Begin(kind);
                }
                else
                {
                    released = _lease;
                    _lease = null;
                }
            }

            released?.Dispose();
        }

        public void OnCompleted() => Stop();

        public void OnError(Exception error) => Stop();

        public void Dispose() => Stop();

        private void Stop()
        {
            IDisposable? lease;
            IDisposable? subscription;
            lock (_sync)
            {
                _stopped = true;
                lease = _lease;
                subscription = _subscription;
                _lease = null;
                _subscription = null;
            }

            lease?.Dispose();
            subscription?.Dispose();
        }
    }
}
