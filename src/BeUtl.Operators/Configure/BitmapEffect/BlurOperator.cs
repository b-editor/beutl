using BeUtl.Graphics.Effects.OpenCv;
using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.BitmapEffect;

public sealed class BlurOperator : BitmapEffectOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Blur>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<PixelSize>(Blur.KernelSizeProperty, new PixelSize(25, 25)));
        initializing.Add(new Setter<bool>(Blur.FixImageSizeProperty, false));
    }
}
