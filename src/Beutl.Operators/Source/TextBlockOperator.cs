using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class TextBlockOperator : StyledSourcePublisher
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<TextBlock>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(TextBlock.SizeProperty, 24));
        initializing.Add(new Setter<IBrush?>(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White)));
        initializing.Add(new Setter<FontFamily>(TextBlock.FontFamilyProperty, FontFamily.Default));
        initializing.Add(new Setter<FontStyle>(TextBlock.FontStyleProperty, FontStyle.Normal));
        initializing.Add(new Setter<FontWeight>(TextBlock.FontWeightProperty, FontWeight.Regular));
        initializing.Add(new Setter<float>(TextBlock.SpacingProperty, 0));
        initializing.Add(new Setter<Thickness>(TextBlock.MarginProperty, default));
        initializing.Add(new Setter<string>(TextBlock.TextProperty, string.Empty));
    }
}
