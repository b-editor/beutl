using BeUtl.Collections;

namespace BeUtl.Styling;

public sealed class Styles : CoreList<IStyle>
{
    public IStyleInstance? Instance(IStyleable target)
    {
        IStyleInstance? baseStyle = null;

        foreach (IStyle item in AsSpan())
        {
            baseStyle = item.Instance(target, baseStyle);
        }

        return baseStyle;
    }
}
