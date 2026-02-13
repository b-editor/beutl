using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Utilities;

public class ScaleMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _scaleSocket;
    private readonly InputSocket<float> _scaleXSocket;
    private readonly InputSocket<float> _scaleYSocket;

    public ScaleMatrixNode()
    {
        _scaleSocket = AddInput<float>("Scale");
        _scaleXSocket = AddInput<float>("ScaleX");
        _scaleYSocket = AddInput<float>("ScaleY");
    }

    public override Matrix GetMatrix(NodeEvaluationContext context)
    {
        return Matrix.CreateScale(_scaleSocket.Value * _scaleXSocket.Value, _scaleSocket.Value * _scaleYSocket.Value);
    }
}
