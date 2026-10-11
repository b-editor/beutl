using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Reactive.Bindings;

namespace Beutl.Api;

// Shared API state is observed on the UI thread and unsubscribed during background teardown.
// ReactivePropertySlim's observer list needs the same gate for notifications, subscribe and dispose.
internal sealed class SynchronizedReadOnlyReactiveProperty<T> : IReadOnlyReactiveProperty<T>
{
    private readonly object _gate = new();
    private readonly ReadOnlyReactivePropertySlim<T> _property;

    public SynchronizedReadOnlyReactiveProperty(IObservable<T> source)
    {
        _property = source.Synchronize(_gate).ToReadOnlyReactivePropertySlim();
        _property.PropertyChanged += OnPropertyChanged;
    }

    // Subscribers can need a lock already held by a Value reader. Reading the
    // latest value must not wait for the gate held while notifying subscribers.
    public T Value => _property.Value;

    object? IReadOnlyReactiveProperty.Value => Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            IDisposable subscription = _property.Subscribe(observer);
            return Disposable.Create(() =>
            {
                lock (_gate) subscription.Dispose();
            });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _property.PropertyChanged -= OnPropertyChanged;
            _property.Dispose();
            PropertyChanged = null;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
