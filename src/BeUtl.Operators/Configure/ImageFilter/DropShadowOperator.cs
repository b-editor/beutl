using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.ImageFilter;

public sealed class DropShadowOperator : ImageFilterOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<DropShadow>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<Point>(DropShadow.PositionProperty, new Point(10, 10)));
        initializing.Add(new Setter<Vector>(DropShadow.SigmaProperty, new Vector(10, 10)));
        initializing.Add(new Setter<Color>(DropShadow.ColorProperty, Colors.Black));
        initializing.Add(new Setter<bool>(DropShadow.ShadowOnlyProperty, false));
    }
}
