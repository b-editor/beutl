using BeUtl.Animation;

namespace BeUtl.Styling;
#pragma warning disable CA1816

public class StyleSetterInstance<T> : ISetterInstance
    where T : IStyleable
{
    private IStyleable? _target;
    private StyleSetter<T>? _setter;
    private IStyleInstance? _inner;
    private IStyleable? _targetValue;

    public StyleSetterInstance(StyleSetter<T> setter, IStyleable target)
    {
        _setter = setter;
        _target = target;
        setter.ValueChanged += Setter_ValueChanged;

        if (setter.Value != null)
        {
            _targetValue = CreateOrGetTargetValue(setter.Value.TargetType);
            _inner = setter.Value.Instance(_targetValue);
            _targetValue.Styles.Add(_inner.Source);
            _targetValue.StyleApplied(_inner);
            target.SetValue(Property, _targetValue);
        }
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
        if (_setter != null)
            _setter.ValueChanged -= Setter_ValueChanged;

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
        T? value = Target.GetValue(Setter.Property);
        if (value?.GetType().IsAssignableTo(type) == true)
        {
            return value;
        }

        return (IStyleable)Activator.CreateInstance(type)!;
    }

    private void Setter_ValueChanged(object? sender, EventArgs e)
    {
        if (_targetValue != null && _inner != null)
        {
            _targetValue.InvalidateStyles();
            _targetValue.Styles.Remove(_inner.Source);
        }

        if (Setter.Value != null)
        {
            _targetValue = CreateOrGetTargetValue(Setter.Value.TargetType)!;
            _inner = Setter.Value.Instance(_targetValue);
            _targetValue.Styles.Add(_inner.Source);
            _targetValue.StyleApplied(_inner);
            Target.SetValue(Property, _targetValue);
        }
    }
}
