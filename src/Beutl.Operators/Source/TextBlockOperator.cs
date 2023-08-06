using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class TextBlockOperator : DrawablePublishOperator<TextBlock>
{
    public Setter<float> Size { get; set; } = new Setter<float>(TextBlock.SizeProperty, 24);

    public Setter<IBrush?> Foreground { get; set; } = new Setter<IBrush?>(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White));

    public Setter<FontFamily> FontFamily { get; set; } = new Setter<FontFamily>(TextBlock.FontFamilyProperty, Media.FontFamily.Default);

    public Setter<FontStyle> FontStyle { get; set; } = new Setter<FontStyle>(TextBlock.FontStyleProperty, Media.FontStyle.Normal);

    public Setter<FontWeight> FontWeight { get; set; } = new Setter<FontWeight>(TextBlock.FontWeightProperty, Media.FontWeight.Regular);

    public Setter<float> Spacing { get; set; } = new Setter<float>(TextBlock.SpacingProperty, 0);

    public Setter<Thickness> Margin { get; set; } = new Setter<Thickness>(TextBlock.MarginProperty, default);

    public Setter<string> Text { get; set; } = new Setter<string>(TextBlock.TextProperty, string.Empty);

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);
}
