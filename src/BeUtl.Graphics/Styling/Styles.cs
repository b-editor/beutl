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

    public void Replace(IList<IStyle> source)
    {
        List<IStyle>? toRemove = null;

        foreach (IStyle item in AsSpan())
        {
            toRemove ??= new List<IStyle>();

            toRemove.Add(item);
        }

        if (toRemove != null)
        {
            RemoveAll(toRemove);
        }

        AddRange(source);
    }
}
