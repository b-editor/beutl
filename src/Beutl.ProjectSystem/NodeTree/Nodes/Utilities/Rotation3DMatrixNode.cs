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
        _rotationXSocket = AsInput(Rotation3DTransform.RotationXProperty).AcceptNumber();
        _rotationYSocket = AsInput(Rotation3DTransform.RotationYProperty).AcceptNumber();
        _rotationZSocket = AsInput(Rotation3DTransform.RotationZProperty).AcceptNumber();
        _centerXSocket = AsInput(Rotation3DTransform.CenterXProperty).AcceptNumber();
        _centerYSocket = AsInput(Rotation3DTransform.CenterYProperty).AcceptNumber();
        _centerZSocket = AsInput(Rotation3DTransform.CenterZProperty).AcceptNumber();
        _depthSocket = AsInput(Rotation3DTransform.DepthProperty).AcceptNumber();
    }

    public override Matrix GetMatrix(EvaluationContext context)
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
