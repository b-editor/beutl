using BeUtl.Animation;

namespace BeUtl.Styling;
#pragma warning disable CA1816

public class StyleSetterInstance<T> : ISetterInstance
{
    private IStyleable? _target;
    private StyleSetter<T>? _setter;
    private IStyleInstance? _inner;
    private IStyleable? _targetValue;
    private IDisposable? _disposable;

    public StyleSetterInstance(StyleSetter<T> setter, IStyleable target)
    {
        _setter = setter;
        _target = target;
        _disposable = setter.Subscribe(Setter_ValueChanged);
    }

    public CoreProperty<T> Property => Setter.Property;

    public StyleSetter<T> Setter => _setter ?? throw new InvalidOperationException();

    public IStyleable Target => _target ?? throw new InvalidOperationException();

    CoreProperty ISetterInstance.Property => Property;

    ISetter ISetterInstance.Setter => Setter;

    public void Apply(IClock clock)
    {
        if (_inner != null && _target != null)
        {
            _target.SetValue(Property, _targetValue);
            _inner.Apply(clock);
        }
    }

    public void Begin()
    {
        if (_inner != null)
        {
            _inner.Begin();
        }
    }

    public void Dispose()
    {
        _target?.ClearValue(Property);
        _targetValue?.InvalidateStyles();
        _disposable?.Dispose();

        _disposable = null;
        _setter = null;
        _target = null;
    }

    public void End()
    {
        if (_inner != null)
        {
            _inner.End();
        }
    }

    private IStyleable CreateOrGetTargetValue(Type type)
    {
        object? value = Target.GetValue(Setter.Property);
        if (value?.GetType().IsAssignableTo(type) == true)
        {
            return (IStyleable)value;
        }

        return (IStyleable)Activator.CreateInstance(type)!;
    }

    private void Setter_ValueChanged(Style? value)
    {
        if (value?.TargetType?.IsAssignableTo(typeof(T)) == false)
        {
            throw new InvalidCastException($"Unable to cast object of type {value?.TargetType} to type {typeof(T)}.");
        }

        if (_targetValue != null && _inner != null)
        {
            _targetValue.InvalidateStyles();
            _targetValue.Styles.Remove(_inner.Source);
        }

        if (value != null)
        {
            _targetValue = CreateOrGetTargetValue(value.TargetType)!;
            _inner = value.Instance(_targetValue);
            _targetValue.Styles.Add(_inner.Source);
            _targetValue.StyleApplied(_inner);
            Target.SetValue(Property, _targetValue);
        }
    }
}
