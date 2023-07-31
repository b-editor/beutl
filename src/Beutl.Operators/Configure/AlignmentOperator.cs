using Beutl.Graphics;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Configure;

public sealed class AlignmentOperator : SourceStyler
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<AlignmentX>(Drawable.AlignmentXProperty, AlignmentX.Center));
        initializing.Add(new Setter<AlignmentY>(Drawable.AlignmentYProperty, AlignmentY.Center));
        initializing.Add(new Setter<RelativePoint>(Drawable.TransformOriginProperty, RelativePoint.Center));
    }
}
