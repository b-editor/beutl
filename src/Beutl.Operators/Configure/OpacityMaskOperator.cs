using Beutl.Graphics;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Configure;

public sealed class OpacityMaskOperator : SourceStyler
{
    public Setter<IBrush?> OpacityMask { get; set; } = new(Drawable.OpacityMaskProperty, null);

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }
}
