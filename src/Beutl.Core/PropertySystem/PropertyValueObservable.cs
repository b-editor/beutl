using Beutl.Reactive;

namespace Beutl;

// The value of a property on subscription and then each new value, as CoreObjectSubject gives a CoreProperty's.
internal sealed class PropertyValueObservable<T>(IProperty<T> property) : LightweightObservableBase<T>
{
    protected override void Initialize() => property.ValueChanged += OnValueChanged;

    protected override void Deinitialize() => property.ValueChanged -= OnValueChanged;

    protected override void Subscribed(IObserver<T> observer, bool first) => observer.OnNext(property.Value);

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T> e) => PublishNext(e.NewValue);
}
