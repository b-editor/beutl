using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure;

public sealed class AlignmentOperator : StreamStyler
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<AlignmentX>(Drawable.CanvasAlignmentXProperty, AlignmentX.Left));
        initializing.Add(new Setter<AlignmentY>(Drawable.CanvasAlignmentYProperty, AlignmentY.Top));
        initializing.Add(new Setter<AlignmentX>(Drawable.AlignmentXProperty, AlignmentX.Left));
        initializing.Add(new Setter<AlignmentY>(Drawable.AlignmentYProperty, AlignmentY.Top));
    }
}
