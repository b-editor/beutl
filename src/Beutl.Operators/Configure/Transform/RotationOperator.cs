using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Transform;

public sealed class RotationOperator : TransformOperator<RotationTransform>
{
    public Setter<float> Rotation { get; set; } = new(RotationTransform.RotationProperty);
}
