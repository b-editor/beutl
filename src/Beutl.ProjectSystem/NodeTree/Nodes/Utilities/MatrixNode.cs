using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities;

public abstract class MatrixNode : Node
{
    private static readonly CoreProperty<MultiplicationOperator> OperatorProperty;
    private readonly OutputSocket<Matrix> _outputSocket;
    private readonly InputSocket<Matrix> _inputSocket;
    private readonly NodeItem<MultiplicationOperator> _operatorSocket;

    static MatrixNode()
    {
        OperatorProperty = ConfigureProperty<MultiplicationOperator, MatrixNode>(o => o.Operator)
            .DefaultValue(MultiplicationOperator.Prepend)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("operator")
            .Register();
    }

    public MatrixNode()
    {
        _outputSocket = AsOutput<Matrix>("Matrix");
        _inputSocket = AsInput<Matrix>("Matrix");
        _operatorSocket = AsProperty(OperatorProperty);
    }

#pragma warning disable CA1822 // メンバーを static に設定します
    private MultiplicationOperator Operator
    {
        get => MultiplicationOperator.Prepend;
        set { }
    }
#pragma warning restore CA1822 // メンバーを static に設定します

    public override void Evaluate(NodeEvaluationContext context)
    {
        Matrix matrix = GetMatrix(context);

        if (_inputSocket.Connection != null)
        {
            Matrix value = _inputSocket.Value;
            switch (_operatorSocket.Value)
            {
                case MultiplicationOperator.Prepend:
                    _outputSocket.Value = matrix * value;
                    break;
                case MultiplicationOperator.Append:
                    _outputSocket.Value = value * matrix;
                    break;
            }
        }
        else
        {
            _outputSocket.Value = matrix;
        }
    }

    public abstract Matrix GetMatrix(NodeEvaluationContext context);
}
