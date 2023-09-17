using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class TextBlockOperator : DrawablePublishOperator<TextBlock>
{
    public Setter<float> Size { get; set; } = new Setter<float>(TextBlock.SizeProperty, 24);

    public Setter<FontFamily?> FontFamily { get; set; } = new Setter<FontFamily?>(TextBlock.FontFamilyProperty, Media.FontFamily.Default);

    public Setter<FontStyle> FontStyle { get; set; } = new Setter<FontStyle>(TextBlock.FontStyleProperty, Media.FontStyle.Normal);

    public Setter<FontWeight> FontWeight { get; set; } = new Setter<FontWeight>(TextBlock.FontWeightProperty, Media.FontWeight.Regular);

    public Setter<float> Spacing { get; set; } = new Setter<float>(TextBlock.SpacingProperty, 0);

    public Setter<string?> Text { get; set; } = new Setter<string?>(TextBlock.TextProperty, string.Empty);

    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, new TransformGroup());

    public Setter<AlignmentX> AlignmentX { get; set; } = new(Drawable.AlignmentXProperty, Media.AlignmentX.Center);

    public Setter<AlignmentY> AlignmentY { get; set; } = new(Drawable.AlignmentYProperty, Media.AlignmentY.Center);

    public Setter<RelativePoint> TransformOrigin { get; set; } = new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<IPen?> Pen { get; set; } = new(TextBlock.PenProperty);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.FillProperty, new SolidColorBrush(Colors.White));

    public Setter<FilterEffect?> FilterEffect { get; set; } = new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);
}
