using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

public sealed class RotationTransform : Transform
{
    public RotationTransform()
    {
        ScanProperties<RotationTransform>();
    }

    public RotationTransform(float rotation) : this()
    {
        Rotation.CurrentValue = rotation;
    }

    public IProperty<float> Rotation { get; } = Property.CreateAnimatable<float>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        float rot = context.Get(Rotation);
        return Matrix.CreateRotation(MathUtilities.Deg2Rad(rot));
    }

    public static RotationTransform FromRadians(float radians)
    {
        return new RotationTransform(MathUtilities.Rad2Deg(radians));
    }
}
