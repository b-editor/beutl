using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Reactive.Bindings;

namespace Beutl.Api;

// Shared API state is observed on the UI thread and unsubscribed during background teardown.
// Keep one forwarding subscriber on the slim property. ReplaySubject manages user
// subscriptions and drains callbacks outside its registration lock.
internal sealed class SynchronizedReadOnlyReactiveProperty<T> : IReadOnlyReactiveProperty<T>
{
    private readonly object _gate = new();
    private readonly ReadOnlyReactivePropertySlim<T> _property;
    private readonly ReplaySubject<T> _notifications = new(1);
    private readonly IDisposable _forwardSubscription;

    public SynchronizedReadOnlyReactiveProperty(IObservable<T> source)
    {
        lock (_gate)
        {
            _property = new ReadOnlyReactivePropertySlim<T>(source.Synchronize(_gate));
            _forwardSubscription = _property.Subscribe(_notifications);
            _property.PropertyChanged += OnPropertyChanged;
        }
    }

    // Subscribers can need a lock already held by a Value reader. Reading the
    // latest value must not wait for the gate held while notifying subscribers.
    public T Value => _property.Value;

    object? IReadOnlyReactiveProperty.Value => Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        if (_property.IsDisposed)
        {
            observer.OnCompleted();
            return Disposable.Empty;
        }

        return _notifications.Subscribe(observer);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _property.PropertyChanged -= OnPropertyChanged;
            _property.Dispose();
            _forwardSubscription.Dispose();
            // Completion releases live observers. Keep the replay cache available
            // for a subscription that raced with disposal, like the retained Value.
            PropertyChanged = null;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
