using System.Reactive;
using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Validation;

namespace Beutl.Styling;

public class Setter<T> : LightweightObservableBase<T?>, ISetter
{
    private CoreProperty<T>? _property;
    private CorePropertyMetadata<T>? _metadata;
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
        _metadata = property.GetMetadata<CorePropertyMetadata<T>>(property.OwnerType);
        if (_metadata.HasDefaultValue)
        {
            Value = _metadata.DefaultValue;
        }
        _isDefault = true;
    }

    public Setter(CoreProperty<T> property, T? value)
    {
        _property = property;
        _metadata = property.GetMetadata<CorePropertyMetadata<T>>(property.OwnerType);
        Value = value;
        _isDefault = false;
    }

    public CorePropertyMetadata<T> Metadata
    {
        get => _metadata ?? throw new InvalidOperationException();
        set => _metadata = value;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set
        {
            _property = value;
            _metadata = value.GetMetadata<CorePropertyMetadata<T>>(value.OwnerType);
            if (_isDefault)
            {
                if (_metadata.HasDefaultValue)
                {
                    Value = _metadata.DefaultValue;
                }

                _isDefault = true;
            }
        }
    }

    public T? Value
    {
        get => _value;
        set
        {
            if (_metadata?.Validator is IValidator<T> validator)
            {
                validator.TryCoerce(new ValidationContext(null, _property), ref value);
            }

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

                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    CorePropertyMetadata ISetter.Metadata => Metadata;

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    IAnimation? ISetter.Animation => _animation;

    public event EventHandler? Invalidated;

    public ISetterInstance Instance(ICoreObject target)
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
