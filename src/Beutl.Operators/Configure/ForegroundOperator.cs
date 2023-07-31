using Beutl.Graphics;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Configure;

public sealed class ForegroundOperator : SourceStyler
{
    public Setter<IBrush?> Foreground { get; set; } = new(Drawable.ForegroundProperty, null);

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }
}
