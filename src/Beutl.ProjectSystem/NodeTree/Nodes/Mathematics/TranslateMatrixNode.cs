using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Mathematics;

public class TranslateMatrixNode : Node
{
    private readonly InputSocket<Matrix> _inputSocket;
    private readonly OutputSocket<Matrix> _outputSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public TranslateMatrixNode()
    {
        _inputSocket = AsInput<Matrix>("Input");
        _outputSocket = AsOutput<Matrix>("Output");
        _xSocket = AsInput(TranslateTransform.XProperty);
        _ySocket = AsInput(TranslateTransform.YProperty);
    }

    public override void Evaluate(EvaluationContext context)
    {
        var first = Matrix.CreateTranslation(_xSocket.Value, _ySocket.Value);

        if (_inputSocket.Connection != null)
        {
            _outputSocket.Value = first * _inputSocket.Value;
        }
        else
        {
            _outputSocket.Value = first * Matrix.Identity;
        }
    }
}
