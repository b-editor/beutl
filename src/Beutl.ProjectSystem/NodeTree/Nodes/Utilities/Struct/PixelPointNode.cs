using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelPointNode : Node
{
    private readonly OutputSocket<PixelPoint> _valueSocket;
    private readonly InputSocket<int> _xSocket;
    private readonly InputSocket<int> _ySocket;

    public PixelPointNode()
    {
        _valueSocket = AddOutput<PixelPoint>("PixelPoint");
        _xSocket = AddInput<int>("X");
        _ySocket = AddInput<int>("Y");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelPoint(_xSocket.Value, _ySocket.Value);
    }
}
