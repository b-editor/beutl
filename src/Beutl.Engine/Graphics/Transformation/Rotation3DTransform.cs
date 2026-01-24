using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(Strings.Rotation3D), ResourceType = typeof(Strings))]
public sealed class Rotation3DTransform : Transform
{
    public Rotation3DTransform()
    {
        ScanProperties<Rotation3DTransform>();
    }

    public Rotation3DTransform(
        float rotationX,
        float rotationY,
        float rotationZ,
        float centerX,
        float centerY,
        float centerZ) : this()
    {
        RotationX.CurrentValue = rotationX;
        RotationY.CurrentValue = rotationY;
        RotationZ.CurrentValue = rotationZ;
        CenterX.CurrentValue = centerX;
        CenterY.CurrentValue = centerY;
        CenterZ.CurrentValue = centerZ;
    }

    public IProperty<float> RotationX { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> RotationY { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> RotationZ { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> CenterZ { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> Depth { get; } = Property.CreateAnimatable(500f);

    public override Matrix CreateMatrix(RenderContext context)
    {
        float centerX = context.Get(CenterX);
        float centerY = context.Get(CenterY);
        float centerZ = context.Get(CenterZ);
        float rotationX = context.Get(RotationX);
        float rotationY = context.Get(RotationY);
        float rotationZ = context.Get(RotationZ);
        float depth = context.Get(Depth);

        Matrix4x4 matrix44 = Matrix4x4.Identity;
        float centerSum = centerX + centerY + centerZ;

        if (MathF.Abs(centerSum) > float.Epsilon) matrix44 *= Matrix4x4.CreateTranslation(-centerX, -centerY, -centerZ);

        if (rotationX != 0) matrix44 *= Matrix4x4.CreateRotationX(MathUtilities.Deg2Rad(rotationX));
        if (rotationY != 0) matrix44 *= Matrix4x4.CreateRotationY(MathUtilities.Deg2Rad(rotationY));
        if (rotationZ != 0) matrix44 *= Matrix4x4.CreateRotationZ(MathUtilities.Deg2Rad(rotationZ));

        if (MathF.Abs(centerSum) > float.Epsilon) matrix44 *= Matrix4x4.CreateTranslation(centerX, centerY, centerZ);

        if (depth != 0)
        {
            Matrix4x4 perspectiveMatrix = Matrix4x4.Identity;
            perspectiveMatrix.M34 = -1 / depth;
            matrix44 *= perspectiveMatrix;
        }

        var matrix = new Matrix(
            matrix44.M11,
            matrix44.M12,
            matrix44.M14,
            matrix44.M21,
            matrix44.M22,
            matrix44.M24,
            matrix44.M41,
            matrix44.M42,
            matrix44.M44);

        return matrix;
    }
}
