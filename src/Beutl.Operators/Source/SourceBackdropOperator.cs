using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceBackdropOperator() : PublishOperator<SourceBackdrop>(
[
    SourceBackdrop.ClearProperty,
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
]);
