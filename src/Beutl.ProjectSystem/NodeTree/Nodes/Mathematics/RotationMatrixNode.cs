using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Mathematics;

public class RotationMatrixNode : Node
{
    private readonly InputSocket<Matrix> _inputSocket;
    private readonly OutputSocket<Matrix> _outputSocket;
    private readonly InputSocket<float> _rotationSocket;

    public RotationMatrixNode()
    {
        _inputSocket = AsInput<Matrix>("Input");
        _outputSocket = AsOutput<Matrix>("Output");
        _rotationSocket = AsInput(RotationTransform.RotationProperty);
    }

    public override void Evaluate(EvaluationContext context)
    {
        var first = Matrix.CreateRotation(MathUtilities.ToRadians(_rotationSocket.Value));

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
