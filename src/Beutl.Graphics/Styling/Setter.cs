using System.Reactive;
using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Reactive;

namespace Beutl.Styling;

public class Setter<T> : LightweightObservableBase<T?>, ISetter
{
    private CoreProperty<T>? _property;
    private T? _value;
    private IAnimation<T>? _animation;
    private bool _isDefault;

    public Setter()
    {
        _isDefault = true;
    }

    public Setter(CoreProperty<T> property)
    {
        _property = property;
        if (property.GetMetadata<CorePropertyMetadata<T>>(property.OwnerType) is { HasDefaultValue: true } metadata)
        {
            Value = metadata.DefaultValue;
        }
        _isDefault = true;
    }

    public Setter(CoreProperty<T> property, T? value)
    {
        _property = property;
        Value = value;
        _isDefault = false;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set
        {
            _property = value;
            if (_isDefault)
            {
                if (_property.GetMetadata<CorePropertyMetadata<T>>(_property.OwnerType) is { HasDefaultValue: true } metadata)
                {
                    Value = metadata.DefaultValue;
                }

                _isDefault = true;
            }
        }
    }

    // Todo: Validation
    public T? Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                _isDefault = false;
                if (_value is IAffectsRender oldValue)
                {
                    oldValue.Invalidated -= Value_Invalidated;
                }

                _value = value;
                PublishNext(value);

                Invalidated?.Invoke(this, EventArgs.Empty);
                if (value is IAffectsRender newValue)
                {
                    newValue.Invalidated += Value_Invalidated;
                }
            }
        }
    }

    public IAnimation<T>? Animation
    {
        get => _animation;
        set
        {
            if (_animation != value)
            {
                if (_animation != null)
                {
                    _animation.Invalidated -= Animation_Invalidated;
                }

                _animation = value;

                if (value != null)
                {
                    value.Invalidated += Animation_Invalidated;
                }
            }
        }
    }

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    IAnimation? ISetter.Animation => _animation;

    public event EventHandler? Invalidated;

    public ISetterInstance Instance(IStyleable target)
    {
        return new SetterInstance<T>(this, target);
    }

    public IObservable<Unit> GetObservable()
    {
        return this.Select(i => Unit.Default);
    }

    protected override void Subscribed(IObserver<T?> observer, bool first)
    {
        observer.OnNext(_value);
    }

    protected override void Deinitialize()
    {
    }

    protected override void Initialize()
    {
    }

    private void Value_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void Animation_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
