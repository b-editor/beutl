using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PackageTools.UI.Services;

/// <summary>
/// Owns asynchronous work that must finish before its backing resources are disposed.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<AsyncOperationLifetime>();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Action _cancelPendingRequests;
    private readonly Func<ValueTask> _disposeResources;
    private readonly TimeSpan _shutdownDeadline;
    private readonly HashSet<Task> _operations = [];
    private Task? _disposeTask;
    private bool _stopping;

    public AsyncOperationLifetime(
        Action cancelPendingRequests,
        Func<ValueTask> disposeResources,
        TimeSpan? shutdownDeadline = null)
    {
        _cancelPendingRequests = cancelPendingRequests
            ?? throw new ArgumentNullException(nameof(cancelPendingRequests));
        _disposeResources = disposeResources
            ?? throw new ArgumentNullException(nameof(disposeResources));
        _shutdownDeadline = shutdownDeadline ?? TimeSpan.FromSeconds(30);
        if (_shutdownDeadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownDeadline));
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
        TaskCompletionSource? proxy = null;
        Task disposeTask;
        lock (_gate)
        {
            _stopping = true;
            if (_disposeTask is null)
            {
                proxy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = proxy.Task;
            }
            disposeTask = _disposeTask;
        }

        if (proxy is not null)
        {
            Task teardown = Task.Run(DisposeCoreAsync);
            _ = CompleteDisposeAsync(proxy, teardown);
        }

        return new ValueTask(disposeTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource proxy, Task teardown)
    {
        try
        {
            await teardown.WaitAsync(_shutdownDeadline).ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (TimeoutException)
        {
            s_logger.LogWarning(
                "Package-tools shutdown exceeded {Deadline}; cleanup will continue after callbacks and operations drain.",
                _shutdownDeadline);
            proxy.TrySetResult();
            _ = ObserveDeferredTeardownAsync(teardown);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
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
        Exception? cancellationFailure = null;
        try
        {
            // Cancel the transport before signaling operation cancellation. The
            // latter invokes user callbacks synchronously; doing it first lets
            // an operation observe cancellation and resume before the transport
            // cancellation hook has run, making shutdown ordering nondeterministic.
            _cancelPendingRequests();
        }
        catch (Exception ex)
        {
            cancellationFailure = ex;
        }

        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (Exception ex)
        {
            cancellationFailure ??= ex;
        }

        try
        {
            await DrainOperationsAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeResourcesAsync().ConfigureAwait(false);
        }

        if (cancellationFailure is not null)
            throw cancellationFailure;
    }

    private async Task DrainOperationsAsync()
    {
        while (true)
        {
            Task[] operations;
            lock (_gate)
            {
                operations = _operations.ToArray();
            }

            if (operations.Length == 0)
                return;

            try
            {
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
            catch
            {
                // Owners report operation failures. Disposal only needs to observe them.
            }

            lock (_gate)
            {
                _operations.RemoveWhere(task => task.IsCompleted);
            }
        }
    }

    private static async Task ObserveDeferredTeardownAsync(Task teardown)
    {
        try
        {
            await teardown.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Deferred package-tools resource cleanup failed.");
        }
    }

    private async Task DisposeResourcesAsync()
    {
        try
        {
            await _disposeResources().ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }
}
