using Beutl.Graphics.Transformation;

namespace Beutl.Operators.Configure.Transform;

public sealed class RotationOperator : TransformOperator<RotationTransform>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return RotationTransform.RotationProperty;
    }
}
