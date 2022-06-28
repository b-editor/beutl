using System.ComponentModel;
using System.Reactive.Subjects;

using BeUtl.Reactive;

namespace BeUtl;

internal sealed class CoreObjectSubject<T> : LightweightObservableBase<T>
{
    private readonly CoreProperty<T> _property;
    private readonly ICoreObject _object;

    public CoreObjectSubject(ICoreObject o, CoreProperty<T> property)
    {
        _object = o;
        _property = property;
        o.PropertyChanged += Object_PropertyChanged;
    }

    protected override void Deinitialize()
    {
        _object.PropertyChanged -= Object_PropertyChanged;
    }

    protected override void Initialize()
    {
        _object.PropertyChanged += Object_PropertyChanged;
    }

    protected override void Subscribed(IObserver<T> observer, bool first)
    {
        observer.OnNext(_object.GetValue(_property));
    }

    private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _property.Name) return;

        T value = _object.GetValue(_property);
        PublishNext(value);
    }
}
