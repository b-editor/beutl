using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelRectNode : Node
{
    private readonly OutputSocket<PixelRect> _valueSocket;
    private readonly InputSocket<PixelPoint> _positionSocket;
    private readonly InputSocket<PixelSize> _sizeSocket;

    public PixelRectNode()
    {
        _valueSocket = AddOutput<PixelRect>("PixelRect");
        _positionSocket = AddInput<PixelPoint>("Position");
        _sizeSocket = AddInput<PixelSize>("Size");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelRect(_positionSocket.Value, _sizeSocket.Value);
    }
}
