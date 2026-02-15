using System.Numerics;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeTree.Rendering;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class Rotation3DMatrixNode : MatrixNode
{
    public Rotation3DMatrixNode()
    {
        RotationX = AddInput<float>("RotationX");
        RotationY = AddInput<float>("RotationY");
        RotationZ = AddInput<float>("RotationZ");
        CenterX = AddInput<float>("CenterX");
        CenterY = AddInput<float>("CenterY");
        CenterZ = AddInput<float>("CenterZ");
        Depth = AddInput<float>("Depth");
    }

    public InputSocket<float> RotationX { get; }

    public InputSocket<float> RotationY { get; }

    public InputSocket<float> RotationZ { get; }

    public InputSocket<float> CenterX { get; }

    public InputSocket<float> CenterY { get; }

    public InputSocket<float> CenterZ { get; }

    public InputSocket<float> Depth { get; }

    private static Matrix ComputeMatrix(
        float rotX, float rotY, float rotZ,
        float centerX, float centerY, float centerZ,
        float depth)
    {
        Matrix4x4 matrix44 = Matrix4x4.Identity;
        float centerSum = centerX + centerY + centerZ;

        if (MathF.Abs(centerSum) > float.Epsilon)
            matrix44 *= Matrix4x4.CreateTranslation(-centerX, -centerY, -centerZ);

        if (rotX != 0) matrix44 *= Matrix4x4.CreateRotationX(MathUtilities.Deg2Rad(rotX));
        if (rotY != 0) matrix44 *= Matrix4x4.CreateRotationY(MathUtilities.Deg2Rad(rotY));
        if (rotZ != 0) matrix44 *= Matrix4x4.CreateRotationZ(MathUtilities.Deg2Rad(rotZ));

        if (MathF.Abs(centerSum) > float.Epsilon)
            matrix44 *= Matrix4x4.CreateTranslation(centerX, centerY, centerZ);

        if (depth != 0)
        {
            Matrix4x4 perspectiveMatrix = Matrix4x4.Identity;
            perspectiveMatrix.M34 = -1 / depth;
            matrix44 *= perspectiveMatrix;
        }

        return new Matrix(
            matrix44.M11, matrix44.M12, matrix44.M14,
            matrix44.M21, matrix44.M22, matrix44.M24,
            matrix44.M41, matrix44.M42, matrix44.M44);
    }

    public partial class Resource
    {
        protected override Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
        {
            return ComputeMatrix(
                RotationX, RotationY, RotationZ,
                CenterX, CenterY, CenterZ,
                Depth);
        }
    }
}
