using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public class SkewMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _skewXSocket;
    private readonly InputSocket<float> _skewYSocket;

    public SkewMatrixNode()
    {
        _skewXSocket = AddInput<float>("SkewX").AcceptNumber();
        _skewYSocket = AddInput<float>("SkewY").AcceptNumber();
    }

    public override Matrix GetMatrix(NodeEvaluationContext context) => Matrix.CreateSkew(MathUtilities.Deg2Rad(_skewXSocket.Value), MathUtilities.Deg2Rad(_skewYSocket.Value));
}
