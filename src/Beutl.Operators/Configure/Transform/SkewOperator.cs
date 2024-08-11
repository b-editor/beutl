using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Transform;

public sealed class SkewOperator : TransformOperator<SkewTransform>
{
    public Setter<float> SkewX { get; set; } = new(SkewTransform.SkewXProperty);

    public Setter<float> SkewY { get; set; } = new(SkewTransform.SkewYProperty);
}
