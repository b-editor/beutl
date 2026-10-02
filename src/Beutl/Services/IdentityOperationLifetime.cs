namespace Beutl.Services;

/// <summary>
/// Fences asynchronous work which belongs to the current authenticated account.
/// An operation keeps the account revision it entered with; publication and an
/// identity switch share the same gate, so a late callback is either admitted
/// before the switch (and finishes before the switch clears the UI) or rejected
/// after it. The identity cancellation token is linked to the operation token.
/// </summary>
internal sealed class IdentityOperationLifetime : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Generation> _generations = [];
    private Generation _current;
    private long _revision;
    private bool _disposed;

    public IdentityOperationLifetime()
    {
        _current = new Generation(0);
        _generations.Add(0, _current);
    }

    public Operation? TryEnter(AsyncOperationLifetime.Operation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if (_disposed)
            {
                operation.Dispose();
                return null;
            }
            if (_current.ClearPending)
            {
                operation.Dispose();
                return null;
            }

            Generation generation = _current;
            generation.Active++;
            return new Operation(this, operation, generation);
        }
    }

    /// <summary>Admits the parent operation and identity revision as one critical section.</summary>
    public Operation? TryEnter(AsyncOperationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lock (_gate)
        {
            if (_disposed)
                return null;
            if (_current.ClearPending)
                return null;

            AsyncOperationLifetime.Operation? operation = lifetime.TryEnter();
            if (operation is null)
                return null;

            Generation generation = _current;
            generation.Active++;
            return new Operation(this, operation, generation);
        }
    }

    /// <summary>
    /// Atomically advances the account revision, cancels work from the old
    /// revision, and clears account-scoped UI state while publication is fenced.
    /// </summary>
    public void Switch(Action clearAccountState)
    {
        ArgumentNullException.ThrowIfNull(clearAccountState);
        Generation? previous = null;
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                previous = _current;
                previous.Retired = true;
                previous.Cancelling = true;
                _revision = checked(_revision + 1);
                _current = new Generation(_revision);
                _generations.Add(_revision, _current);
                // Keep the gate through clearing so no new-generation
                // operation can publish into state that is about to be reset.
                clearAccountState();
            }
        }
        finally
        {
            if (previous is not null)
                StartCancellation(previous);
        }
    }

    /// <summary>
    /// Advances the identity fence immediately, then clears UI-owned state on
    /// the supplied scheduler before admitting work for the new identity.
    /// </summary>
    public void SwitchDeferred(
        Action<Action> scheduleClear,
        Action clearAccountState,
        Action? afterClear = null)
    {
        ArgumentNullException.ThrowIfNull(scheduleClear);
        ArgumentNullException.ThrowIfNull(clearAccountState);

        Generation previous;
        Generation next;
        lock (_gate)
        {
            if (_disposed)
                return;

            previous = _current;
            previous.Retired = true;
            previous.Cancelling = true;
            _revision = checked(_revision + 1);
            next = new Generation(_revision) { ClearPending = true };
            _current = next;
            _generations.Add(_revision, next);
        }

        try
        {
            scheduleClear(() =>
            {
                bool cleared = false;
                lock (_gate)
                {
                    if (!_disposed
                        && ReferenceEquals(_current, next)
                        && next.ClearPending)
                    {
                        try
                        {
                            clearAccountState();
                            cleared = true;
                        }
                        finally
                        {
                            next.ClearPending = false;
                        }
                    }
                }

                if (cleared)
                    afterClear?.Invoke();
            });
        }
        finally
        {
            StartCancellation(previous);
        }
    }

    public void Dispose()
    {
        Generation[] generations;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            generations = _generations.Values.ToArray();
            foreach (Generation generation in generations)
            {
                generation.Retired = true;
                // Keep the running state established by an earlier cancellation task.
                if (generation.CancellationTask is null)
                    generation.Cancelling = true;
            }
        }

        foreach (Generation generation in generations)
            StartCancellation(generation);
    }

    private void StartCancellation(Generation generation)
    {
        Task? cancellationTask;
        lock (_gate)
        {
            if (!generation.Cancelling)
                return;
            if (generation.CancellationTask is null)
                generation.CancellationTask = CancelGenerationAsync(generation);
            cancellationTask = generation.CancellationTask;
        }

        // CancellationTokenSource marks IsCancellationRequested before invoking
        // user callbacks. Wait only for that mark, not for callbacks themselves,
        // so callers observe the fence synchronously without inheriting a stuck
        // callback's latency.
        SpinWait.SpinUntil(
            () => generation.Cancellation.IsCancellationRequested || cancellationTask.IsCompleted,
            TimeSpan.FromSeconds(1));
    }

    private async Task CancelGenerationAsync(Generation generation)
    {
        try
        {
            await Task.Run(generation.Cancellation.Cancel).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            lock (_gate)
            {
                generation.Cancelling = false;
                TryRetire_NoLock(generation);
            }
        }
    }

    private bool TryPublish(Operation operation, Action publication)
    {
        lock (_gate)
        {
            if (_disposed || operation.Closed || !ReferenceEquals(operation.Generation, _current))
                return false;

            // Keep the identity gate through the underlying publication gate
            // and callback. Switch() therefore cannot clear state underneath a
            // callback which has already been admitted.
            return operation.Parent.TryPublish(publication);
        }
    }

    private bool IsCurrent(Operation operation)
    {
        lock (_gate)
            return !_disposed && !operation.Closed && ReferenceEquals(operation.Generation, _current);
    }

    private void Close(Operation operation)
    {
        lock (_gate)
        {
            if (operation.Closed)
                return;
            operation.Closed = true;
        }
    }

    private void Exit(Operation operation)
    {
        lock (_gate)
        {
            if (operation.Generation.Active > 0)
                operation.Generation.Active--;
            TryRetire_NoLock(operation.Generation);
        }
    }

    private void TryRetire_NoLock(Generation generation)
    {
        if (generation.Retired && generation.Active == 0 && !generation.Cancelling
            && _generations.Remove(generation.Revision))
        {
            generation.Cancellation.Dispose();
        }
    }

    internal sealed class Generation(long revision)
    {
        public long Revision { get; } = revision;
        public CancellationTokenSource Cancellation { get; } = new();
        public int Active;
        public bool Retired;
        public bool Cancelling;
        public Task? CancellationTask;
        public bool ClearPending;
    }

    internal sealed class Operation : IDisposable
    {
        private readonly IdentityOperationLifetime _owner;
        private readonly object _cancellationGate = new();
        private readonly CancellationTokenRegistration _parentCancellation;
        private readonly CancellationTokenRegistration _identityCancellation;
        private CancellationTokenSource? _cancellation;
        private int _activeCancellationCalls;
        private bool _cancellationDisposalRequested;
        internal readonly AsyncOperationLifetime.Operation Parent;
        internal readonly Generation Generation;
        internal bool Closed;
        private int _disposed;

        internal Operation(
            IdentityOperationLifetime owner,
            AsyncOperationLifetime.Operation parent,
            Generation generation)
        {
            _owner = owner;
            Parent = parent;
            Generation = generation;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            CancellationToken = cancellation.Token;
            _parentCancellation = parent.CancellationToken.UnsafeRegister(
                static state => ((Operation)state!).CancelLinked(), this);
            _identityCancellation = generation.Cancellation.Token.UnsafeRegister(
                static state => ((Operation)state!).CancelLinked(), this);
        }

        public CancellationToken CancellationToken { get; }

        public bool TryPublish(Action publication)
            => _owner.TryPublish(this, publication);

        public bool IsCurrent
            => _owner.IsCurrent(this);

        public void ClosePublication()
            => _owner.Close(this);

        public void Cancel()
            => Parent.Cancel();

        private void CancelLinked()
        {
            CancellationTokenSource? source;
            lock (_cancellationGate)
            {
                source = _cancellation;
                if (source is not null)
                    _activeCancellationCalls++;
            }

            if (source is null)
                return;

            try
            {
                source.Cancel();
            }
            finally
            {
                bool dispose;
                lock (_cancellationGate)
                {
                    _activeCancellationCalls--;
                    dispose = _activeCancellationCalls == 0 && _cancellationDisposalRequested;
                    if (dispose)
                        _cancellation = null;
                }

                if (dispose)
                    source.Dispose();
            }
        }

        private void DisposeCancellation()
        {
            // Dispose on a linked CTS waits for its forwarding registrations. A callback may
            // be waiting for this operation to exit, so unlink without waiting for it.
            _parentCancellation.Unregister();
            _identityCancellation.Unregister();

            CancellationTokenSource? source = null;
            lock (_cancellationGate)
            {
                _cancellationDisposalRequested = true;
                if (_activeCancellationCalls == 0)
                {
                    source = _cancellation;
                    _cancellation = null;
                }
            }

            // Active forwarding calls retain the source until their user callbacks return.
            source?.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.Close(this);
            DisposeCancellation();
            _owner.Exit(this);
            Parent.Dispose();
        }
    }
}
