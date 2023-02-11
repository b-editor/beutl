using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Mathematics;

public class TranslateMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public TranslateMatrixNode()
    {
        _xSocket = AsInput(TranslateTransform.XProperty);
        _ySocket = AsInput(TranslateTransform.YProperty);
    }

    public override Matrix GetMatrix(EvaluationContext context) => Matrix.CreateTranslation(_xSocket.Value, _ySocket.Value);
}
