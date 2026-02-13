using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelSizeNode : Node
{
    private readonly OutputSocket<PixelSize> _valueSocket;
    private readonly InputSocket<int> _widthSocket;
    private readonly InputSocket<int> _heightSocket;

    public PixelSizeNode()
    {
        _valueSocket = AddOutput<PixelSize>("PixelSize");
        _widthSocket = AddInput<int>("Width");
        _heightSocket = AddInput<int>("Height");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelSize(_widthSocket.Value, _heightSocket.Value);
    }
}
