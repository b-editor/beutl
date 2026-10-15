using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

/// <summary>
/// Admits asynchronous operations while a view-model is alive, owns their cancellation token,
/// admits publication before shutdown begins, keeps admitted publication in the
/// drain, and allows disposal to await every admitted operation. Publication admission and
/// shutdown are linearized by the gate. If a publication acquires the gate before
/// <see cref="DisposeAsync()"/> marks the lifetime as stopping, its callback may run after
/// shutdown has started, but remains in the drain until that callback returns.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private static readonly TimeSpan s_defaultShutdownDeadline = TimeSpan.FromSeconds(30);
    private static readonly ILogger s_logger = Log.CreateLogger<AsyncOperationLifetime>();
    private readonly object _gate = new();
    private readonly TimeSpan _shutdownDeadline;
    private CancellationTokenSource? _cancellation = new();
    private TaskCompletionSource? _drained;
    private Task? _stopCancellationTask;
    private readonly List<CancellationTokenSource> _deferredOperationCancellations = [];
    private Exception? _cancellationFailure;
    private Task? _disposeTask;
    private int _activeOperations;
    private int _activePublications;
    private bool _stopping;

    public AsyncOperationLifetime(TimeSpan? shutdownDeadline = null)
    {
        _shutdownDeadline = shutdownDeadline ?? s_defaultShutdownDeadline;
        if (_shutdownDeadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownDeadline));
    }

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

    // Starts the stop; the returned task completes when no work is active.
    private Task BeginStop_NoLock(CancellationTokenSource? cancellation)
    {
        Task drained = HasNoActiveWork_NoLock()
            ? Task.CompletedTask
            : (_drained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        _stopCancellationTask = CreateCancellationTask(cancellation);
        return drained;
    }

    private void CancelStop(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception ex)
        {
            RecordCancellationFailure(ex);
        }
    }

    private Task CreateCancellationTask(CancellationTokenSource? cancellation)
        => cancellation is null
            ? Task.CompletedTask
            : Task.Factory.StartNew(
                () => CancelStop(cancellation),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

    // Waits for the drain and the cancellation, disposes the operation sources whose disposal waited for the
    // cancellation, and returns the failures to report.
    private async Task<List<Exception>?> CompleteStopDrainAsync(Task drained, Task cancellationTask)
    {
        List<Exception>? failures = null;
        try
        {
            await Task.WhenAll(drained, cancellationTask).ConfigureAwait(false);
        }
        catch
        {
            AddFailures(drained, ref failures);
            AddFailures(cancellationTask, ref failures);
        }

        Exception? failure;
        List<CancellationTokenSource>? deferredCancellations;
        lock (_gate)
        {
            failure = _cancellationFailure;
            deferredCancellations = _deferredOperationCancellations.Count == 0
                ? null
                : [.. _deferredOperationCancellations];
            _deferredOperationCancellations.Clear();
        }

        if (deferredCancellations is not null)
        {
            foreach (CancellationTokenSource deferred in deferredCancellations)
                deferred.Dispose();
        }

        if (failure is not null)
        {
            IEnumerable<Exception> cancellationFailures = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [failure];
            foreach (Exception cancellationFailure in cancellationFailures)
            {
                if (failures?.Contains(cancellationFailure) != true)
                    (failures ??= []).Add(cancellationFailure);
            }
        }

        return failures;
    }

    public ValueTask DisposeAsync()
        => DisposeAsync(static () => ValueTask.CompletedTask);

    public ValueTask DisposeAsync(Func<ValueTask> disposeResources)
        => DisposeAsync(static () => { }, disposeResources);

    public ValueTask DisposeAsync(Action cancelAdditionalWork, Func<ValueTask> disposeResources)
    {
        ArgumentNullException.ThrowIfNull(cancelAdditionalWork);
        ArgumentNullException.ThrowIfNull(disposeResources);
        TaskCompletionSource? proxy = null;
        Task? drained = null;
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                proxy = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = proxy.Task;
                _stopping = true;
                drained = BeginStop_NoLock(_cancellation);
            }

            disposeTask = _disposeTask;
        }

        if (proxy is not null)
        {
            Task teardown = Task.Run(() => DisposeCoreAsync(
                drained!,
                cancelAdditionalWork,
                disposeResources));
            _ = CompleteDisposeAsync(proxy, teardown);
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(
        Task drained,
        Action cancelAdditionalWork,
        Func<ValueTask> disposeResources)
    {
        Task additionalCancellation = Task.Run(cancelAdditionalWork);
        List<Exception>? failures = await CompleteStopDrainAsync(drained, _stopCancellationTask!)
            .ConfigureAwait(false);

        try
        {
            await additionalCancellation.ConfigureAwait(false);
        }
        catch
        {
            AddFailures(additionalCancellation, ref failures);
        }

        try
        {
            await disposeResources().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }

        CancellationTokenSource? ownedCancellation;
        lock (_gate)
        {
            ownedCancellation = _cancellation;
            _cancellation = null;
        }
        ownedCancellation?.Dispose();

        if (failures is { Count: 1 })
            throw failures[0];
        if (failures is { Count: > 1 })
            throw new AggregateException(failures);
    }

    private static void AddFailures(Task task, ref List<Exception>? failures)
    {
        if (task.Exception is not { } aggregate)
            return;

        foreach (Exception exception in aggregate.Flatten().InnerExceptions)
        {
            if (failures?.Contains(exception) != true)
                (failures ??= []).Add(exception);
        }
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
                "Asynchronous operation disposal exceeded {Deadline}; resource cleanup will continue.",
                _shutdownDeadline);
            proxy.TrySetResult();
            _ = ObserveDeferredDisposeAsync(teardown);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private async Task ObserveDeferredDisposeAsync(Task teardown)
    {
        try
        {
            await teardown.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Deferred asynchronous operation resource cleanup failed.");
        }
    }

    private bool TryPublish(Operation operation, Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (operation.PublicationGate)
        {
            lock (_gate)
            {
                if (_stopping || !operation.CanPublish)
                    return false;

                // This increment is the publication's linearization point. The operation
                // gate remains held through the callback.
                _activePublications++;
            }

            try
            {
                publication();
                return true;
            }
            finally
            {
                ExitPublication();
            }
        }
    }

    private void ExitPublication()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            _activePublications--;
            if (_stopping && HasNoActiveWork_NoLock())
                drained = _drained;
        }
        drained?.TrySetResult();
    }

    private bool HasNoActiveWork_NoLock()
        => _activeOperations == 0 && _activePublications == 0;

    private void Exit(CancellationTokenSource operationCancellation)
    {
        TaskCompletionSource? drained = null;
        bool disposeCancellation = true;
        lock (_gate)
        {
            _activeOperations--;
            if (_stopping && _stopCancellationTask is { IsCompleted: false })
            {
                _deferredOperationCancellations.Add(operationCancellation);
                disposeCancellation = false;
            }
            if (_stopping && HasNoActiveWork_NoLock())
            {
                drained = _drained;
            }
        }
        if (disposeCancellation)
            operationCancellation.Dispose();
        drained?.TrySetResult();
    }

    private void RecordCancellationFailure(Exception exception)
    {
        lock (_gate)
        {
            _cancellationFailure = _cancellationFailure is null
                ? exception
                : new AggregateException(_cancellationFailure, exception);
        }
    }

    /// <summary>
    /// One admitted operation. Its token dies with the view-model, and separately
    /// with <see cref="Cancel"/>, so a caller can abandon a single long request
    /// without shutting down everything else the view-model has admitted.
    /// </summary>
    internal sealed class Operation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        internal object PublicationGate { get; } = new();
        private AsyncOperationLifetime? _owner;
        private int _publicationClosed;

        internal Operation(AsyncOperationLifetime owner, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
            CancellationToken = cancellation.Token;
        }

        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Attempts to admit a publication. The callback is invoked outside the lifetime gate;
        /// once admitted, it is included in shutdown draining even if it throws.
        /// </summary>
        public bool TryPublish(Action publication)
            => _owner?.TryPublish(this, publication) == true;

        internal bool CanPublish => Volatile.Read(ref _publicationClosed) == 0;

        public void Cancel()
        {
            if (_owner is not { } owner)
                return;

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                owner.RecordCancellationFailure(ex);
            }
        }

        public void Dispose()
        {
            // Stop new admission without waiting for an already-running
            // publication. The owner's publication count keeps that callback
            // in the lifetime drain until it returns.
            Interlocked.Exchange(ref _publicationClosed, 1);
            if (Interlocked.Exchange(ref _owner, null) is not { } owner)
                return;

            owner.Exit(_cancellation);
        }
    }
}
