using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;

namespace Beutl.Styling;

[ExcludeFromCodeCoverage]
public sealed class Styles : CoreList<IStyle>
{
    public IStyleInstance? Instance(ICoreObject target)
    {
        IStyleInstance? baseStyle = null;

        foreach (IStyle item in GetMarshal().Value)
        {
            baseStyle = item.Instance(target, baseStyle);
        }

        return baseStyle;
    }
}
