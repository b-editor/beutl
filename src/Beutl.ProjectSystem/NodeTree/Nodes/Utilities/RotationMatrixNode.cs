using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public class RotationMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _rotationSocket;

    public RotationMatrixNode()
    {
        _rotationSocket = AsInput(RotationTransform.RotationProperty).AcceptNumber();
    }

    public override Matrix GetMatrix(NodeEvaluationContext context)
    {
        return Matrix.CreateRotation(MathUtilities.ToRadians(_rotationSocket.Value));
    }
}
