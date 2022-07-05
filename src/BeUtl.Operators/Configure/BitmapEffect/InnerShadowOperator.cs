using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Language;
using BeUtl.Media;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<Point>(InnerShadow.PositionProperty)
        {
            DefaultValue = new Point(10, 10),
            Header = StringResources.Common.PositionObservable,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<PixelSize>(InnerShadow.KernelSizeProperty)
        {
            DefaultValue = new PixelSize(25, 25),
            Minimum = new PixelSize(1, 1),
            Header = new ResourceReference<string>("S.Common.KernelSize").GetResourceObservable()!,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<Color>(InnerShadow.ColorProperty)
        {
            DefaultValue = Colors.Black,
            Header = StringResources.Common.ColorObservable,
            IsAnimatable = true
        });
    }
}
