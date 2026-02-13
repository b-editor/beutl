using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

public class OutputNode : Node
{
    private readonly RenderNodeDrawable _drawable = new();

    public OutputNode()
    {
        InputSocket = AddInput<RenderNode>("Output");
    }

    protected InputSocket<RenderNode> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RenderNode? input = InputSocket.Value;
        _drawable.Node = input;
        context.AddRenderable(_drawable);
    }
}
