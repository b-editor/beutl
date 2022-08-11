using BeUtl.Graphics.Transformation;

namespace BeUtl.Operators.Configure.Transform;

public sealed class Rotation3DOperator : TransformOperator<Rotation3DTransform>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Rotation3DTransform.RotationXProperty;
        yield return Rotation3DTransform.RotationYProperty;
        yield return Rotation3DTransform.RotationZProperty;
        yield return Rotation3DTransform.CenterXProperty;
        yield return Rotation3DTransform.CenterYProperty;
        yield return Rotation3DTransform.CenterZProperty;
        yield return Rotation3DTransform.DepthProperty;
    }
}
