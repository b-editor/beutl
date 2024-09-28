using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class TextBlockOperator() : PublishOperator<TextBlock>(
[
    (TextBlock.SizeProperty, 24f),
    (TextBlock.FontFamilyProperty, Media.FontFamily.Default),
    (TextBlock.FontStyleProperty, Media.FontStyle.Normal),
    (TextBlock.FontWeightProperty, Media.FontWeight.Regular),
    (TextBlock.SpacingProperty, 0f),
    (TextBlock.SplitByCharactersProperty),
    (TextBlock.TextProperty, string.Empty),
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    TextBlock.PenProperty,
    (Drawable.FillProperty, () => new SolidColorBrush(Colors.White)),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
]);
