using BeUtl.Collections;

namespace BeUtl.Styling;

public class Style : IStyle
{
    private readonly CoreList<ISetter> _setters = new();

    public virtual Type TargetType { get; set; } = typeof(Styleable);

    public IList<ISetter> Setters => _setters;

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
