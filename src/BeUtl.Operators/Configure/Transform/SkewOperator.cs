using BeUtl.Graphics.Transformation;

namespace BeUtl.Operators.Configure.Transform;

public sealed class SkewOperator : TransformOperator<SkewTransform>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return SkewTransform.SkewXProperty;
        yield return SkewTransform.SkewYProperty;
    }
}
