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
        _scaleSocket = AsInput(ScaleTransform.ScaleProperty);
        _scaleXSocket = AsInput(ScaleTransform.ScaleXProperty);
        _scaleYSocket = AsInput(ScaleTransform.ScaleYProperty);
    }

    public override Matrix GetMatrix(EvaluationContext context)
    {
        return Matrix.CreateScale(_scaleSocket.Value * _scaleXSocket.Value, _scaleSocket.Value * _scaleYSocket.Value);
    }
}
