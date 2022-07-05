using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Language;
using BeUtl.Media;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<Point>(Border.OffsetProperty)
        {
            DefaultValue = new Point(),
            Header = new ResourceReference<string>("S.Common.Offset").GetResourceObservable()!,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<int>(Border.ThicknessProperty)
        {
            DefaultValue = 8,
            Minimum = 0,
            Header = new ResourceReference<string>("S.Common.Thickness").GetResourceObservable()!,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<Color>(Border.ColorProperty)
        {
            DefaultValue = Colors.White,
            Header = StringResources.Common.ColorObservable,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<Border.MaskTypes>(Border.MaskTypeProperty)
        {
            DefaultValue = Border.MaskTypes.None,
            Header = new ResourceReference<string>("S.Common.MaskType").GetResourceObservable()!,
        });
        initializing.Add(new SetterDescription<Border.BorderStyles>(Border.StyleProperty)
        {
            DefaultValue = Border.BorderStyles.Background,
            Header = new ResourceReference<string>("S.Common.BorderStyle").GetResourceObservable()!,
        });
    }
}
