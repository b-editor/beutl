using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.BitmapEffect;

public sealed class BorderOperator : BitmapEffectOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Border>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<Point>(Border.OffsetProperty, new Point()));
        initializing.Add(new Setter<int>(Border.ThicknessProperty, 8));
        initializing.Add(new Setter<Color>(Border.ColorProperty, Colors.White));
        initializing.Add(new Setter<Border.MaskTypes>(Border.MaskTypeProperty, Border.MaskTypes.None));
        initializing.Add(new Setter<Border.BorderStyles>(Border.StyleProperty, Border.BorderStyles.Background));
    }
}
