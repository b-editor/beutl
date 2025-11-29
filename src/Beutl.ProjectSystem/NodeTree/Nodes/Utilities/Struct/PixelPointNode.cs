using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelPointNode : Node
{
    private readonly OutputSocket<PixelPoint> _valueSocket;
    private readonly InputSocket<int> _xSocket;
    private readonly InputSocket<int> _ySocket;

    public PixelPointNode()
    {
        _valueSocket = AsOutput<PixelPoint>("PixelPoint");
        _xSocket = AsInput<int>("X").AcceptNumber();
        _ySocket = AsInput<int>("Y").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelPoint(_xSocket.Value, _ySocket.Value);
    }
}
