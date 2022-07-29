using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.BitmapEffect;

public sealed class InnerShadowOperator : BitmapEffectOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<InnerShadow>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<Point>(InnerShadow.PositionProperty, new Point(10, 10)));
        initializing.Add(new Setter<PixelSize>(InnerShadow.KernelSizeProperty, new PixelSize(25, 25)));
        initializing.Add(new Setter<Color>(InnerShadow.ColorProperty, Colors.Black));
    }
}
