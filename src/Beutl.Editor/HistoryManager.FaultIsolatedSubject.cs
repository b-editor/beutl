using System.Reactive.Disposables;

namespace Beutl.Editor;

public sealed partial class HistoryManager
{
    private sealed class FaultIsolatedSubject<T>(Action<Exception> observerFailure) : IObservable<T>, IDisposable
    {
        private readonly object _gate = new();
        private readonly List<SubscriptionEntry> _subscriptions = [];
        private bool _isCompleted;
        private bool _isDisposed;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            var entry = new SubscriptionEntry(observer);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_isCompleted)
                {
                    observer.OnCompleted();
                    return Disposable.Empty;
                }

                _subscriptions.Add(entry);
            }

            return Disposable.Create(() =>
            {
                lock (_gate)
                {
                    _subscriptions.Remove(entry);
                }
            });
        }

        public void OnNext(T value)
        {
            SubscriptionEntry[] subscriptions;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_isCompleted)
                    return;
                subscriptions = _subscriptions.ToArray();
            }

            foreach (SubscriptionEntry subscription in subscriptions)
            {
                try
                {
                    subscription.Observer.OnNext(value);
                }
                catch (Exception ex)
                {
                    observerFailure(ex);
                }
            }
        }

        public void OnCompleted()
        {
            SubscriptionEntry[] subscriptions;
            lock (_gate)
            {
                if (_isDisposed || _isCompleted)
                    return;
                _isCompleted = true;
                subscriptions = _subscriptions.ToArray();
                _subscriptions.Clear();
            }

            foreach (SubscriptionEntry subscription in subscriptions)
            {
                try
                {
                    subscription.Observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observerFailure(ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _isDisposed = true;
                _subscriptions.Clear();
            }
        }

        private sealed class SubscriptionEntry(IObserver<T> observer)
        {
            public IObserver<T> Observer { get; } = observer;
        }
    }
}
