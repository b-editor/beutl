using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class SizeNode : Node
{
    private readonly OutputSocket<Size> _valueSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;

    public SizeNode()
    {
        _valueSocket = AsOutput<Size>("Size");
        _widthSocket = AsInput<float>("Width").AcceptNumber();
        _heightSocket = AsInput<float>("Height").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Size(_widthSocket.Value, _heightSocket.Value);
    }
}
