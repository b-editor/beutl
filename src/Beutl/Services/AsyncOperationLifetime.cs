namespace Beutl.Services;

/// <summary>
/// Admits asynchronous operations while a view-model is alive, owns their cancellation token,
/// prevents publication after shutdown begins, and allows disposal to await every admitted operation.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation = new();
    private TaskCompletionSource? _drained;
    private int _activeOperations;
    private bool _stopping;

    public Operation? TryEnter()
    {
        lock (_gate)
        {
            if (_stopping || _cancellation is null)
                return null;

            _activeOperations++;
            return new Operation(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token));
        }
    }

    public Task StopAsync()
    {
        CancellationTokenSource? cancellation = null;
        Task drained;
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                cancellation = _cancellation;
            }

            drained = _activeOperations == 0
                ? Task.CompletedTask
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        cancellation?.Cancel();
        return drained;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _cancellation;
            _cancellation = null;
        }
        cancellation?.Dispose();
    }

    private bool TryPublish(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (_gate)
        {
            if (_stopping)
                return false;

            publication();
            return true;
        }
    }

    private void Exit()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_activeOperations <= 0)
                return;

            _activeOperations--;
            if (_stopping && _activeOperations == 0)
            {
                drained = _drained;
            }
        }
        drained?.TrySetResult();
    }

    /// <summary>
    /// One admitted operation. Its token dies with the view-model, and separately
    /// with <see cref="Cancel"/>, so a caller can abandon a single long request
    /// without shutting down everything else the view-model has admitted.
    /// </summary>
    internal sealed class Operation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private AsyncOperationLifetime? _owner;

        internal Operation(AsyncOperationLifetime owner, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
            CancellationToken = cancellation.Token;
        }

        public CancellationToken CancellationToken { get; }

        public bool TryPublish(Action publication)
            => _owner?.TryPublish(publication) == true;

        public void Cancel()
        {
            if (_owner is null)
                return;

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } owner)
                return;

            _cancellation.Dispose();
            owner.Exit();
        }
    }
}
