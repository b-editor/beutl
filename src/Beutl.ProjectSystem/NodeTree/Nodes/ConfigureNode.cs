using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

public abstract class ConfigureNode : Node
{
    public ConfigureNode()
    {
        OutputSocket = AsOutput<ContainerRenderNode?>("Output");
        InputSocket = AsInput<RenderNode?>("Input");
    }

    protected OutputSocket<ContainerRenderNode?> OutputSocket { get; }

    protected InputSocket<RenderNode?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RenderNode? input = InputSocket.Value;
        ContainerRenderNode? output = OutputSocket.Value;

        EvaluateCore(context);
        if (input != null && output != null)
        {
            output.HasChanges = input.HasChanges || output.HasChanges;
            output.RemoveRange(0, output.Children.Count);
            output.AddChild(input);
        }
    }

    protected abstract void EvaluateCore(NodeEvaluationContext context);
}
