namespace Beutl.Media.Proxy;

public sealed partial class ProxyJobQueue
{
    private sealed class WorkItem(ProxyJob job, CancellationTokenSource cancellation) : IDisposable
    {
        private readonly Lock _lock = new();
        private bool _started;
        private TaskCompletionSource? _cancellationCallbacksCompleted;
        private bool _terminalTransitionClaimed;
        private bool _cancellationRequested;
        private bool _cancellationWindowClosed;
        private bool _disposed;

        public ProxyJob Job { get; } = job;

        // Guarded by the queue's _lock (not this WorkItem's _lock). Set true once the item is fully
        // registered, immediately before its wake permit is written; TakeNextDispatchable ignores
        // unpublished items so a partially-constructed job can never be dispatched.
        public bool Published { get; set; }

        public CancellationTokenSource Cancellation { get; } = cancellation;

        // Captured up front so a waiter can observe cancellation even after the source is disposed
        // (reading Cancellation.Token after Dispose throws); the token value stays valid.
        public CancellationToken Token { get; } = cancellation.Token;

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested)
                    return false;

                _started = true;
                return true;
            }
        }

        public bool ResetForRetry()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                    return false;

                _started = false;
                Job.Status = ProxyJobStatus.Queued;
                return true;
            }
        }

        public bool TryCompleteSuccess()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || !_cancellationWindowClosed
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                Job.Status = ProxyJobStatus.Succeeded;
                _terminalTransitionClaimed = true;
                return true;
            }
        }

        public bool TryClaimNonCancellationTerminal(bool cancellationWins)
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || IsTerminal(Job.Status)
                    || (cancellationWins
                        && (_cancellationRequested
                            || Cancellation.IsCancellationRequested)))
                {
                    return false;
                }

                _terminalTransitionClaimed = true;
                return true;
            }
        }

        public bool TryCloseCancellationWindow()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationWindowClosed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                _cancellationWindowClosed = true;
                return true;
            }
        }

        public bool TryClaimCancellationDuringCleanup()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationWindowClosed
                    || !_cancellationRequested
                    && !Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                _cancellationWindowClosed = true;
                return true;
            }
        }

        public bool TryCancelQueued()
        {
            TaskCompletionSource cancellationCompleted;
            lock (_lock)
            {
                if (_disposed
                    || _started
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || _cancellationWindowClosed
                    || IsTerminal(Job.Status))
                    return false;

                _cancellationRequested = true;
                cancellationCompleted = _cancellationCallbacksCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            CancelSource(cancellationCompleted);

            return true;
        }

        public void Cancel()
        {
            TaskCompletionSource cancellationCompleted;
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || _cancellationWindowClosed
                    || IsTerminal(Job.Status))
                    return;

                _cancellationRequested = true;
                cancellationCompleted = _cancellationCallbacksCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            CancelSource(cancellationCompleted);
        }

        public Task WaitForCancellationCallbacksAsync()
        {
            lock (_lock)
            {
                return _cancellationCallbacksCompleted?.Task ?? Task.CompletedTask;
            }
        }

        private void CancelSource(TaskCompletionSource cancellationCompleted)
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cancellationCompleted.TrySetResult();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                Cancellation.Dispose();
            }
        }
    }
}
