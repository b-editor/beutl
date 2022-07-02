using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;
using BeUtl.Language;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Source;

public sealed class RoundedRectOperation : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<RoundedRect>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(Drawable.WidthProperty)
        {
            DefaultValue = 100,
            Minimum = 0,
            IsAnimatable = true,
            Header = StringResources.Common.WidthObservable
        });
        initializing.Add(new SetterDescription<float>(Drawable.HeightProperty)
        {
            DefaultValue = 100,
            Minimum = 0,
            IsAnimatable = true,
            Header = StringResources.Common.HeightObservable
        });
        initializing.Add(new SetterDescription<float>(Ellipse.StrokeWidthProperty)
        {
            DefaultValue = 4000,
            Minimum = 0,
            IsAnimatable = true,
            Header = StringResources.Common.StrokeWidthObservable
        });
        initializing.Add(new SetterDescription<IBrush?>(Drawable.ForegroundProperty)
        {
            DefaultValue = new SolidColorBrush(Colors.White),
            IsAnimatable = true,
            Header = StringResources.Common.ColorObservable
        });
        initializing.Add(new SetterDescription<CornerRadius>(RoundedRect.CornerRadiusProperty)
        {
            DefaultValue = new CornerRadius(25),
            Minimum = new CornerRadius(0),
            IsAnimatable = true,
            Header = StringResources.Common.CornerRadiusObservable
        });
    }
}
