using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RoundedRectOperator() : PublishOperator<RoundedRectShape>(
[
    (Shape.WidthProperty, 100f),
    (Shape.HeightProperty, 100f),
    (RoundedRectShape.CornerRadiusProperty, new CornerRadius(25)),
    (RoundedRectShape.SmoothingProperty, 0f),
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    Shape.PenProperty,
    (Drawable.FillProperty, () => new SolidColorBrush(Colors.White)),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
]);
