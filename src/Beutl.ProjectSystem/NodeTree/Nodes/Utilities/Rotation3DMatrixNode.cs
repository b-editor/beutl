using System.Numerics;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public class Rotation3DMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _rotationXSocket;
    private readonly InputSocket<float> _rotationYSocket;
    private readonly InputSocket<float> _rotationZSocket;
    private readonly InputSocket<float> _centerXSocket;
    private readonly InputSocket<float> _centerYSocket;
    private readonly InputSocket<float> _centerZSocket;
    private readonly InputSocket<float> _depthSocket;

    public Rotation3DMatrixNode()
    {
        _rotationXSocket = AddInput<float>("RotationX");
        _rotationYSocket = AddInput<float>("RotationY");
        _rotationZSocket = AddInput<float>("RotationZ");
        _centerXSocket = AddInput<float>("CenterX");
        _centerYSocket = AddInput<float>("CenterY");
        _centerZSocket = AddInput<float>("CenterZ");
        _depthSocket = AddInput<float>("Depth");
    }

    public override Matrix GetMatrix(NodeEvaluationContext context)
    {
        Matrix4x4 matrix44 = Matrix4x4.Identity;
        float centerSum = _centerXSocket.Value + _centerYSocket.Value + _centerZSocket.Value;

        if (MathF.Abs(centerSum) > float.Epsilon) matrix44 *= Matrix4x4.CreateTranslation(-_centerXSocket.Value, -_centerYSocket.Value, -_centerZSocket.Value);

        if (_rotationXSocket.Value != 0) matrix44 *= Matrix4x4.CreateRotationX(MathUtilities.Deg2Rad(_rotationXSocket.Value));
        if (_rotationYSocket.Value != 0) matrix44 *= Matrix4x4.CreateRotationY(MathUtilities.Deg2Rad(_rotationYSocket.Value));
        if (_rotationZSocket.Value != 0) matrix44 *= Matrix4x4.CreateRotationZ(MathUtilities.Deg2Rad(_rotationZSocket.Value));

        if (MathF.Abs(centerSum) > float.Epsilon) matrix44 *= Matrix4x4.CreateTranslation(_centerXSocket.Value, _centerYSocket.Value, _centerZSocket.Value);

        if (_depthSocket.Value != 0)
        {
            Matrix4x4 perspectiveMatrix = Matrix4x4.Identity;
            perspectiveMatrix.M34 = -1 / _depthSocket.Value;
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
