using System.Reactive.Linq;
using System.Reactive;

using BeUtl.Animation;
using BeUtl.Collections;
using BeUtl.Reactive;

namespace BeUtl.Styling;

public class StyleSetter<T> : LightweightObservableBase<Style?>, ISetter
{
    private CoreProperty<T>? _property;
    private Style? _value;

    public StyleSetter()
    {
    }

    public StyleSetter(CoreProperty<T> property, Style? value)
    {
        _property = property;
        Value = value;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set => _property = value;
    }

    public Style? Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                PublishNext(value);
            }
        }
    }

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    ICoreReadOnlyList<IAnimation> ISetter.Animations => throw new InvalidOperationException();

    public ISetterInstance Instance(IStyleable target)
    {
        if (Value?.TargetType?.IsAssignableTo(typeof(T)) == false)
        {
            throw new InvalidCastException($"Unable to cast object of type {Value?.TargetType} to type {typeof(T)}.");
        }
        return new StyleSetterInstance<T>(this, target);
    }

    public IObservable<Unit> GetObservable()
    {
        return this.Select(i => Unit.Default);
    }

    protected override void Subscribed(IObserver<Style?> observer, bool first)
    {
        observer.OnNext(_value);
    }

    protected override void Initialize()
    {
    }

    protected override void Deinitialize()
    {
    }
}
