using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public class RotationMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _rotationSocket;

    public RotationMatrixNode()
    {
        _rotationSocket = AddInput<float>("Rotation").AcceptNumber();
    }

    public override Matrix GetMatrix(NodeEvaluationContext context)
    {
        return Matrix.CreateRotation(MathUtilities.Deg2Rad(_rotationSocket.Value));
    }
}
