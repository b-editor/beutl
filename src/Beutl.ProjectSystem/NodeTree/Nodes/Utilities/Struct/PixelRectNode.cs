using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelRectNode : Node
{
    private readonly OutputSocket<PixelRect> _valueSocket;
    private readonly InputSocket<PixelPoint> _positionSocket;
    private readonly InputSocket<PixelSize> _sizeSocket;

    public PixelRectNode()
    {
        _valueSocket = AsOutput<PixelRect>("PixelRect");
        _positionSocket = AsInput<PixelPoint>("Position").AcceptNumber();
        _sizeSocket = AsInput<PixelSize>("Size").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelRect(_positionSocket.Value, _sizeSocket.Value);
    }
}
