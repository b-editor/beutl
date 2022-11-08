using Beutl.Graphics.Transformation;

namespace Beutl.Operators.Configure.Transform;

public sealed class SkewOperator : TransformOperator<SkewTransform>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return SkewTransform.SkewXProperty;
        yield return SkewTransform.SkewYProperty;
    }
}
