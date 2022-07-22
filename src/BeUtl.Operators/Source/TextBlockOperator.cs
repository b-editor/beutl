using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;
using BeUtl.Language;

namespace BeUtl.Operators.Source;

public sealed class TextBlockOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<TextBlock>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(TextBlock.SizeProperty)
        {
            DefaultValue = 24,
            Minimum = 0,
            IsAnimatable = true,
            Header = StringResources.Common.SizeObservable
        });
        initializing.Add(new SetterDescription<IBrush?>(Drawable.ForegroundProperty)
        {
            DefaultValue = new SolidColorBrush(Colors.White),
            IsAnimatable = true,
            Header = StringResources.Common.ColorObservable
        });
        initializing.Add(new SetterDescription<FontFamily>(TextBlock.FontFamilyProperty)
        {
            DefaultValue = FontFamily.Default,
            Header = StringResources.Common.FontFamilyObservable
        });
        initializing.Add(new SetterDescription<FontStyle>(TextBlock.FontStyleProperty)
        {
            DefaultValue = FontStyle.Normal,
            Header = StringResources.Common.FontStyleObservable
        });
        initializing.Add(new SetterDescription<FontWeight>(TextBlock.FontWeightProperty)
        {
            DefaultValue = FontWeight.Regular,
            Header = StringResources.Common.FontWeightObservable
        });
        initializing.Add(new SetterDescription<float>(TextBlock.SpacingProperty)
        {
            DefaultValue = 0,
            IsAnimatable = true,
            Header = StringResources.Common.CharactorSpacingObservable
        });
        initializing.Add(new SetterDescription<Thickness>(TextBlock.MarginProperty)
        {
            DefaultValue = default,
            Minimum = default,
            IsAnimatable = true,
            Header = StringResources.Common.MarginObservable
        });
        initializing.Add(new SetterDescription<string>(TextBlock.TextProperty)
        {
            DefaultValue = string.Empty,
            Header = StringResources.Common.TextObservable
        });
    }
}
