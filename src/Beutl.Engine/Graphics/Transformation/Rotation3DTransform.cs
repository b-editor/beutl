using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.Rotation3DTransform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Rotation3DTransform : Transform
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
        RotationX.Value = rotationX;
        RotationY.Value = rotationY;
        RotationZ.Value = rotationZ;
        CenterX.Value = centerX;
        CenterY.Value = centerY;
        CenterZ.Value = centerZ;
    }

    [Display(Name = nameof(GraphicsStrings.Rotation3DTransform_RotationX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> RotationX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Rotation3DTransform_RotationY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> RotationY { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Rotation3DTransform_RotationZ), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> RotationZ { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.CenterX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> CenterX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.CenterY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> CenterY { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Rotation3DTransform_CenterZ), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> CenterZ { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Depth), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Depth { get; } = Property.CreateAnimatable(500f);

    public override Matrix CreateMatrix(CompositionContext context)
    {
        float centerX = context.Get(CenterX);
        float centerY = context.Get(CenterY);
        float centerZ = context.Get(CenterZ);
        float rotationX = context.Get(RotationX);
        float rotationY = context.Get(RotationY);
        float rotationZ = context.Get(RotationZ);
        float depth = context.Get(Depth);

        return CreateMatrix(rotationX, rotationY, rotationZ, centerX, centerY, centerZ, depth);
    }

    // Also used by Rotation3DMatrixNode in Beutl.NodeGraph.
    internal static Matrix CreateMatrix(
        float rotationX, float rotationY, float rotationZ,
        float centerX, float centerY, float centerZ,
        float depth)
    {
        Matrix4x4 matrix44 = Matrix4x4.Identity;
        // Each component counts, so a center such as (100, -100, 0) is not taken for the origin. A NaN center is
        // still skipped, as when the components' sum decided.
        bool moveCenter = (centerX != 0 || centerY != 0 || centerZ != 0) && !float.IsNaN(centerX + centerY + centerZ);

        if (moveCenter) matrix44 *= Matrix4x4.CreateTranslation(-centerX, -centerY, -centerZ);

        if (rotationX != 0) matrix44 *= Matrix4x4.CreateRotationX(MathUtilities.Deg2Rad(rotationX));
        if (rotationY != 0) matrix44 *= Matrix4x4.CreateRotationY(MathUtilities.Deg2Rad(rotationY));
        if (rotationZ != 0) matrix44 *= Matrix4x4.CreateRotationZ(MathUtilities.Deg2Rad(rotationZ));

        if (moveCenter) matrix44 *= Matrix4x4.CreateTranslation(centerX, centerY, centerZ);

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
