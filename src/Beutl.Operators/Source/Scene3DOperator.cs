using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public class Scene3DOperator() : PublishOperator<Scene3D>(
[
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    (Drawable.FillProperty, () => new SolidColorBrush(Colors.White)),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
]);
