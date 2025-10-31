using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Utilities;

public class TranslateMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public TranslateMatrixNode()
    {
        _xSocket = AsInput<float>("X").AcceptNumber();
        _ySocket = AsInput<float>("Y").AcceptNumber();
    }

    public override Matrix GetMatrix(NodeEvaluationContext context) => Matrix.CreateTranslation(_xSocket.Value, _ySocket.Value);
}
