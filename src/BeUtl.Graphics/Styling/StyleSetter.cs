using BeUtl.Animation;
using BeUtl.Collections;

namespace BeUtl.Styling;

public class StyleSetter<T> : ISetter
    where T : IStyleable
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

    public event EventHandler? ValueChanged;

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
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    ICoreReadOnlyList<IAnimation> ISetter.Animations => throw new InvalidOperationException();

    public ISetterInstance Instance(IStyleable target)
    {
        return new StyleSetterInstance<T>(this, target);
    }
}
