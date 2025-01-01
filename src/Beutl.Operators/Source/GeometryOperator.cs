using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class GeometryOperator() : PublishOperator<GeometryShape>(
[
    (GeometryShape.DataProperty, () => new PathGeometry()),
    (Drawable.TransformProperty, () => new TransformGroup()),
    (Drawable.AlignmentXProperty, Media.AlignmentX.Left),
    (Drawable.AlignmentYProperty, Media.AlignmentY.Top),
    Drawable.TransformOriginProperty,
    Shape.PenProperty,
    (Drawable.FillProperty, () => new SolidColorBrush(Colors.White)),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
]);
