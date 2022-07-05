using BeUtl.Graphics.Effects.OpenCv;
using BeUtl.Media;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<PixelSize>(Blur.KernelSizeProperty)
        {
            DefaultValue = new PixelSize(25, 25),
            Minimum = new PixelSize(1, 1),
            Header = new ResourceReference<string>("S.Common.KernelSize").GetResourceObservable()!,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<bool>(Blur.FixImageSizeProperty)
        {
            DefaultValue = false,
            Header = new ResourceReference<string>("S.Common.FixImageSize").GetResourceObservable()!,
            IsAnimatable = true
        });
    }
}
