using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PointNode : Node
{
    private readonly OutputSocket<Point> _valueSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public PointNode()
    {
        _valueSocket = AsOutput<Point>("Point");
        _xSocket = AsInput<float>("X").AcceptNumber();
        _ySocket = AsInput<float>("Y").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Point(_xSocket.Value, _ySocket.Value);
    }
}
