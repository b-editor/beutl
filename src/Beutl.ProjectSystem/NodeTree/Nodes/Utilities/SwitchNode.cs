namespace Beutl.NodeTree.Nodes.Utilities;

public class SwitchNode : Node
{
    private readonly OutputSocket<object?> _outputSocket;
    private readonly InputSocket<bool> _switchSocket;
    private readonly InputSocket<object?> _trueSocket;
    private readonly InputSocket<object?> _falseSocket;

    public SwitchNode()
    {
        _outputSocket = AddOutput<object?>("Output");
        _switchSocket = AddInput<bool>("Switch");
        _trueSocket = AddInput<object?>("True");
        _falseSocket = AddInput<object?>("False");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        if (_switchSocket.Value)
        {
            _outputSocket.Value = _trueSocket.Value;
        }
        else
        {
            _outputSocket.Value = _falseSocket.Value;
        }
    }
}
