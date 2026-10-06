using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    internal void BeginShutdown()
    {
        lock (_gate)
        {
            _disposed = true;
            _shutdownFallbackPublicationEnabled = true;
        }
    }

    protected virtual async Task DisposeCoreAsync()
    {
        long deadline = Environment.TickCount64 + DrainDeadlineMilliseconds;
        bool drained = false;
        while (!drained && Environment.TickCount64 < deadline)
        {
            Task[] operations;
            lock (_gate)
            {
                RemoveCompletedOperations_NoLock();
                if (_operations.Count == 0)
                {
                    drained = true;
                }
                operations = _operations.ToArray();
            }

            if (drained)
                break;

            try
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    break;

                await Task.WhenAll(operations).WaitAsync(TimeSpan.FromMilliseconds(remaining)).ConfigureAwait(false);
            }
            catch
            {
            }

            lock (_gate)
            {
                RemoveCompletedOperations_NoLock();
            }
        }

        lock (_gate)
        {
            _drained = true;
        }

        if (drained)
        {
            DisposeResources();
        }
        else
        {
            _logger.LogWarning(
                "Package installer did not drain within the shutdown deadline; "
                + "keeping resources alive until tracked work stops.");
            _ = DisposeResourcesWhenIdleAsync();
        }
    }

    // Waits until every admitted operation has actually stopped before releasing the
    // installer resources, so a slow download or NuGet phase that outlived the drain
    // deadline never sees its cache context or HttpClient disposed underneath it.
    private async Task DisposeResourcesWhenIdleAsync()
    {
        try
        {
            while (true)
            {
                Task[] operations;
                lock (_gate)
                {
                    RemoveCompletedOperations_NoLock();
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
                    // Awaiting observes operation faults; resource disposal must still run.
                }
            }

            DisposeResources();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose package installer resources after tracked work stopped.");
        }
    }

    private void DisposeResources()
    {
        _cacheContext.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    // Waits until every admitted operation has actually stopped, without the drain
    // deadline: when Disposal ended at its deadline with operations still running,
    // their fallback queueing must be observable before the shutdown snapshot of
    // PackageChangesQueue is taken. The timeout keeps the wait bounded even when a
    // tracked operation never completes, so shutdown cannot hang behind it.
    internal async Task WaitUntilIdleAsync(TimeSpan timeout)
    {
        _shutdownFallbackPublicationEnabled = true;
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            long remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                PublishShutdownFallbacks();
                _logger.LogWarning("Package installer did not become idle within the shutdown deadline.");
                return;
            }

            Task[] operations;
            lock (_gate)
            {
                RemoveCompletedOperations_NoLock();
                operations = _operations.ToArray();
            }

            if (operations.Length == 0)
                return;

            try
            {
                await Task.WhenAll(operations).WaitAsync(TimeSpan.FromMilliseconds(remaining)).ConfigureAwait(false);
            }
            catch
            {
                // Awaiting observes operation faults; the loop must keep waiting for
                // every admitted operation to stop before the queue snapshot.
            }
        }
    }

    private void PublishShutdownFallbacks()
    {
        ShutdownFallback[] fallbacks;
        lock (_gate)
        {
            RemoveCompletedOperations_NoLock();
            BeforeShutdownFallbackSnapshot?.Invoke();
            fallbacks = _shutdownFallbacks
                .Select(pair => pair.Value)
                .ToArray();
        }

        foreach (ShutdownFallback fallback in fallbacks)
        {
            try
            {
                fallback.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish a package shutdown fallback.");
            }
        }
    }

    private void RemoveCompletedOperations_NoLock()
    {
        foreach (Task operation in _operations.Where(task => task.IsCompleted).ToArray())
        {
            _operations.Remove(operation);
            _shutdownFallbacks.Remove(operation);
        }
    }

    // Virtual so tests can shorten the drain window; production keeps the 30-second budget.
    protected virtual long DrainDeadlineMilliseconds => 30_000;

    private Task TrackAsync(Func<Task> operation)
        => TrackAsyncCore(operation);

    private Task<T> TrackAsync<T>(Func<Task<T>> operation)
        => TrackAsyncCore(operation);

    public Task TrackInstallOperationAsync(Func<Task> operation)
        => TrackInstallOperationWithShutdownFallbackAsync(operation, null);

    internal Task TrackInstallOperationWithShutdownFallbackAsync(
        Func<Task> operation,
        Action? ensureShutdownFallback)
    {
        ArgumentNullException.ThrowIfNull(operation);
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = proxy.Task;
        ShutdownFallback? shutdownFallback = ensureShutdownFallback is null
            ? null
            : CreateOneShotFallback(ensureShutdownFallback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RemoveCompletedOperations_NoLock();
            // Register before invoking so a re-entrant DisposeAsync drains this transaction.
            _operations.Add(task);
            if (shutdownFallback is not null)
                _shutdownFallbacks.Add(task, shutdownFallback);
        }

        // Invoke outside the lock: a delegate that blocks before its first incomplete await
        // must not hold the gate, or a concurrent DisposeAsync could not even start its
        // drain deadline. The operation's awaits use ConfigureAwait(false) so shutdown can
        // block without deadlocking.
        _ = RunTransactionAsync(operation, proxy, shutdownFallback);
        return task;
    }

    private ShutdownFallback CreateOneShotFallback(Action fallback)
        => new(fallback, _logger);

    internal void TrackSyncOperation(Action operation)
    {
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)), this);
            _operations.Add(proxy.Task);
        }

        PackageInstaller? previous = s_transactionOwner.Value;
        s_transactionOwner.Value = this;
        try
        {
            operation();
        }
        finally
        {
            // The proxy exists only to keep disposal draining until the operation finishes;
            // the caller observes the exception directly, so complete it normally to avoid
            // an unobserved fault when the drain loop removes the completed task.
            proxy.TrySetResult();
            s_transactionOwner.Value = previous;
        }
    }

    internal T TrackSyncOperation<T>(Func<T> operation)
    {
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)), this);
            _operations.Add(proxy.Task);
        }

        PackageInstaller? previous = s_transactionOwner.Value;
        s_transactionOwner.Value = this;
        try
        {
            return operation();
        }
        finally
        {
            // See TrackSyncOperation(Action): the proxy is lifetime-only, so complete it
            // normally even when the operation throws.
            proxy.TrySetResult();
            s_transactionOwner.Value = previous;
        }
    }

    private async Task RunTransactionAsync(
        Func<Task> operation,
        TaskCompletionSource proxy,
        ShutdownFallback? shutdownFallback)
    {
        PackageInstaller? previous = s_transactionOwner.Value;
        s_transactionOwner.Value = this;
        try
        {
            await operation().ConfigureAwait(false);
            if (shutdownFallback is not null)
            {
                // The operation has committed successfully. Disarm first so a
                // shutdown snapshot that already retained this object cannot
                // publish stale recovery work, then remove it and publish the
                // successful proxy atomically under the tracking gate.
                shutdownFallback.Disarm();
                AfterSuccessfulInstallFallbackDisarmed?.Invoke();
            }
            lock (_gate)
            {
                _shutdownFallbacks.Remove(proxy.Task);
                proxy.TrySetResult();
            }
        }
        catch (OperationCanceledException ex)
        {
            if (_shutdownFallbackPublicationEnabled)
                shutdownFallback?.Invoke();
            proxy.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            // Propagate the failure so the caller reports it and queues fallback.
            shutdownFallback?.Invoke();
            proxy.TrySetException(ex);
        }
        finally
        {
            s_transactionOwner.Value = previous;
        }
    }

    private sealed class ShutdownFallback(
        Action fallback,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        private Action? _fallback = fallback;

        public void Disarm()
            => Interlocked.Exchange(ref _fallback, null);

        public void Invoke()
        {
            Action? callback = Interlocked.Exchange(ref _fallback, null);
            if (callback is null)
                return;

            try
            {
                callback();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish a package shutdown fallback.");
            }
        }
    }

    private Task TrackAsyncCore(Func<Task> operation)
    {
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // After draining, reject all work including nested phases.
            if (_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)))
            {
                throw new ObjectDisposedException(nameof(PackageInstaller));
            }
            RemoveCompletedOperations_NoLock();
            // Register before invoking so a concurrent DisposeAsync drains this phase.
            _operations.Add(proxy.Task);
        }

        // Invoke outside the lock so a re-entrant delegate cannot deadlock on _gate.
        _ = RunTrackedAsync(operation, proxy);
        return proxy.Task;
    }

    private Task<T> TrackAsyncCore<T>(Func<Task<T>> operation)
    {
        TaskCompletionSource<T> proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)))
            {
                throw new ObjectDisposedException(nameof(PackageInstaller));
            }
            RemoveCompletedOperations_NoLock();
            // Register before invoking so a concurrent DisposeAsync drains this phase.
            _operations.Add(proxy.Task);
        }

        // Invoke outside the lock so a re-entrant delegate cannot deadlock on _gate.
        _ = RunTrackedAsync(operation, proxy);
        return proxy.Task;
    }

    private async Task RunTrackedAsync(Func<Task> operation, TaskCompletionSource proxy)
    {
        try
        {
            await operation().ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            proxy.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private async Task<T> RunTrackedAsync<T>(Func<Task<T>> operation, TaskCompletionSource<T> proxy)
    {
        try
        {
            T result = await operation().ConfigureAwait(false);
            proxy.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            proxy.TrySetCanceled(ex.CancellationToken);
            return default!;
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
            return default!;
        }
    }
}
