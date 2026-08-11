namespace Beutl.PackageTools.UI.Services;

/// <summary>
/// Owns asynchronous work that must finish before its backing resources are disposed.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Action _cancelPendingRequests;
    private readonly Action _disposeResources;
    private readonly HashSet<Task> _operations = [];
    private Task? _disposeTask;
    private bool _stopping;

    public AsyncOperationLifetime(Action cancelPendingRequests, Action disposeResources)
    {
        _cancelPendingRequests = cancelPendingRequests
            ?? throw new ArgumentNullException(nameof(cancelPendingRequests));
        _disposeResources = disposeResources
            ?? throw new ArgumentNullException(nameof(disposeResources));
    }

    public Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => RunAsync(operation, completion: null, cancellationToken);

    public Task RunAsync(
        Func<CancellationToken, Task> operation,
        Action? completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        CancellationTokenSource linkedCancellation;
        Task task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _operations.RemoveWhere(task => task.IsCompleted);
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
            task = RunCoreAsync(operation, completion, linkedCancellation);
            _operations.Add(task);
        }

        return task;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task RunCoreAsync(
        Func<CancellationToken, Task> operation,
        Action? completion,
        CancellationTokenSource linkedCancellation)
    {
        // Ensure the task is registered before a synchronously completing operation can remove it.
        await Task.Yield();
        try
        {
            await operation(linkedCancellation.Token);
            if (completion != null)
            {
                lock (_gate)
                {
                    if (!_stopping && !linkedCancellation.IsCancellationRequested)
                    {
                        completion();
                    }
                }
            }
        }
        finally
        {
            linkedCancellation.Dispose();
        }
    }

    private async Task DisposeCoreAsync()
    {
        _lifetimeCancellation.Cancel();
        _cancelPendingRequests();

        try
        {
            while (true)
            {
                Task[] operations;
                lock (_gate)
                {
                    operations = _operations.ToArray();
                }

                if (operations.Length == 0)
                    break;

                try
                {
                    await Task.WhenAll(operations).ConfigureAwait(false);
                }
                catch
                {
                    // Awaiting observes operation failures. Each owner reports its own operation error;
                    // resource teardown must still run after every task has drained.
                }

                lock (_gate)
                {
                    _operations.RemoveWhere(task => task.IsCompleted);
                }
            }
        }
        finally
        {
            try
            {
                _disposeResources();
            }
            finally
            {
                _lifetimeCancellation.Dispose();
            }
        }
    }
}
