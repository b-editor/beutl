using BeUtl.Collections;

namespace BeUtl.Styling;

public class Style : IStyle
{
    private readonly CoreList<ISetter> _setters = new();
    private Type _targetType = typeof(Styleable);

    public Style()
    {
        _setters.CollectionChanged += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public virtual Type TargetType
    {
        get => _targetType;
        set
        {
            if (_targetType != value)
            {
                _targetType = value;
                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ICoreList<ISetter> Setters => _setters;

    public event EventHandler? Invalidated;

    public IStyleInstance Instance(IStyleable target, IStyleInstance? baseStyle = null)
    {
        var array = new ISetterInstance[_setters.Count];
        int index = 0;
        foreach (ISetter item in _setters.AsSpan())
        {
            array[index++] = item.Instance(target);
        }

        return new StyleInstance(target, this, array, baseStyle);
    }
}
public sealed class Style<T> : Style
    where T : Styleable
{
    public override Type TargetType
    {
        get => typeof(T);
        set => throw new InvalidOperationException();
    }
}
