using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities;

public abstract class MatrixNode : Node
{
    private readonly OutputSocket<Matrix> _outputSocket;
    private readonly InputSocket<Matrix> _inputSocket;

    public MatrixNode()
    {
        _outputSocket = AddOutput<Matrix>("Output");
        _inputSocket = AddInput<Matrix>("Input");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Matrix matrix = GetMatrix(context);

        if (!_inputSocket.Connection.IsNull)
        {
            Matrix value = _inputSocket.Value;
            _outputSocket.Value = matrix * value;
        }
        else
        {
            _outputSocket.Value = matrix;
        }
    }

    public abstract Matrix GetMatrix(NodeEvaluationContext context);
}
