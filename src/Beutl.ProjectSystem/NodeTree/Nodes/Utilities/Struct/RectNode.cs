using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class RectNode : Node
{
    private readonly OutputSocket<Rect> _valueSocket;
    private readonly InputSocket<Point> _positionSocket;
    private readonly InputSocket<Size> _sizeSocket;

    public RectNode()
    {
        _valueSocket = AsOutput<Rect>("Rect");
        _positionSocket = AsInput<Point>("TopLeft").AcceptNumber();
        _sizeSocket = AsInput<Size>("Size").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Rect(_positionSocket.Value, _sizeSocket.Value);
    }
}
