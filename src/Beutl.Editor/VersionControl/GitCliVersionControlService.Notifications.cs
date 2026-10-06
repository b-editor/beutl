namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task QueueStatusChangedCoreAsync(
        CancellationToken cancellationToken, RepositoryChangeKind changeKind = RepositoryChangeKind.All)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        QueueStatusChanged(status with
        {
            ChangeKind = changeKind,
            NotificationSequence = Interlocked.Increment(ref _statusNotificationSequence),
        });
    }

    private async Task TryQueueStatusChangedCoreAsync()
    {
        try
        {
            await QueueStatusChangedCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to publish version-control status after a durable Git operation.");
        }
    }

    private void QueueStatusChanged(WorkspaceStatus status)
    {
        _statusNotifications.Enqueue(status);
        if (Interlocked.CompareExchange(ref _statusNotificationDrainScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _statusNotificationScheduler(DrainStatusNotifications);
        }
        catch
        {
            Volatile.Write(ref _statusNotificationDrainScheduled, 0);
            throw;
        }
    }

    private static void ScheduleStatusNotificationDrain(Action drain)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((Action)state!).Invoke(),
            drain,
            preferLocal: false);
    }

    private void DrainStatusNotifications()
    {
        while (true)
        {
            while (_statusNotifications.TryDequeue(out WorkspaceStatus? status))
            {
                NotifyStatusChanged(status);
            }

            Volatile.Write(ref _statusNotificationDrainScheduled, 0);
            if (_statusNotifications.IsEmpty
                || Interlocked.CompareExchange(ref _statusNotificationDrainScheduled, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private void NotifyStatusChanged(WorkspaceStatus status)
    {
        if (IsDisposed || StatusChanged is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<WorkspaceStatus> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, status);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a version-control status subscriber.");
            }
        }
    }

    private void EnsureWatcher()
    {
        lock (_lifetimeSync)
        {
            if (IsDisposed
                || !_createWatcherWhenRepositoryAvailable
                || _watcher is not null
                || Repository is null)
            {
                return;
            }

            _watcher = new RepositoryWatcher(Repository);
            _watcher.UpdateRequiredPaths(_requiredTemporaryProjectPaths);
            _watcher.Changed += OnRepositoryChanged;
        }
    }

    private void TryEnsureWatcher()
    {
        try
        {
            EnsureWatcher();
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to start repository watching after initializing version control.");
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        RepositoryChangeKind kind = e is RepositoryChangedEventArgs change ? change.Kind : RepositoryChangeKind.All;
        Interlocked.Or(ref _watcherRefreshPending, (int)kind);
        if (Interlocked.CompareExchange(ref _watcherRefreshScheduled, 1, 0) == 0)
        {
            _ = RefreshStatusFromWatcherAsync();
        }
    }

    private void CaptureRecoverableLock(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);
        while (pending.TryPop(out Exception? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is GitOperationException gitException
                && TryCaptureRecoverableLock(gitException))
            {
                return;
            }

            if (current is AggregateException aggregate)
            {
                for (int i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                {
                    pending.Push(aggregate.InnerExceptions[i]);
                }

                continue;
            }

            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }
        }
    }

    private bool TryCaptureRecoverableLock(GitOperationException exception)
    {
        IGitCliRunner? runner = _runner;
        RepositoryInfo? repository = Repository;
        if (!exception.IsRepositoryLockFailure
            || repository is null
            || runner is null)
        {
            return false;
        }

        RepositoryLockInfo? lockInfo = runner.GetRecoverableRepositoryLock(repository);
        if (lockInfo is null)
        {
            return false;
        }

        RecoverableLock = lockInfo;
        _lockNotificationScheduler(() => NotifyRecoverableLockAvailable(lockInfo));
        return true;
    }

    private void NotifyRecoverableLockAvailable(RepositoryLockInfo lockInfo)
    {
        if (IsDisposed
            || !ReferenceEquals(RecoverableLock, lockInfo)
            || RecoverableLockAvailable is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<RepositoryLockInfo> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, lockInfo);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a recoverable repository-lock subscriber.");
            }
        }
    }

    private static void ScheduleLockNotification(Action notification)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((Action)state!).Invoke(),
            notification,
            preferLocal: false);
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        lock (_runtimeSync)
        {
            _configurationRevision++;
            _cachedAvailability = null;
            _runner = null;
        }
    }

    private async Task RefreshStatusFromWatcherAsync()
    {
        while (true)
        {
            try
            {
                await RunSerializedAsync(
                        () =>
                        {
                            // Consume changes only after acquiring the gate: events accumulated
                            // behind a save/pull need one read. Events during this read need another.
                            var kind = (RepositoryChangeKind)Interlocked.Exchange(ref _watcherRefreshPending, 0);
                            if (Interlocked.Exchange(ref _watcherRefreshInvalidated, 0) != 0)
                            {
                                kind = RepositoryChangeKind.All;
                            }
                            return QueueStatusChangedCoreAsync(CancellationToken.None, kind);
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (IsDisposed)
            {
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _watcherRefreshInvalidated, 1);
                LogWarningBestEffort(
                    ex,
                    "Failed to refresh version-control status after a repository change.");
            }

            // Release between reads so user operations can acquire the gate. If a change races
            // this handoff, either this loop or the event handler schedules the next read.
            Volatile.Write(ref _watcherRefreshScheduled, 0);
            if (IsDisposed
                || Volatile.Read(ref _watcherRefreshPending) == 0
                || Interlocked.CompareExchange(ref _watcherRefreshScheduled, 1, 0) != 0)
            {
                return;
            }
        }
    }
}
