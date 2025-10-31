using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelSizeNode : Node
{
    private readonly OutputSocket<PixelSize> _valueSocket;
    private readonly InputSocket<int> _widthSocket;
    private readonly InputSocket<int> _heightSocket;

    public PixelSizeNode()
    {
        _valueSocket = AsOutput<PixelSize>("PixelSize");
        _widthSocket = AsInput<int>("Width").AcceptNumber();
        _heightSocket = AsInput<int>("Height").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelSize(_widthSocket.Value, _heightSocket.Value);
    }
}
