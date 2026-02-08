using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

public abstract class ConfigureNode : Node
{
    public ConfigureNode()
    {
        OutputSocket = AsOutput<ContainerRenderNode?>("Output");
        InputSocket = AsListInput<RenderNode?>("Input");
    }

    protected OutputSocket<ContainerRenderNode?> OutputSocket { get; }

    protected ListInputSocket<RenderNode?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        var inputs = InputSocket.CollectValues()!;
        ContainerRenderNode? output = OutputSocket.Value;

        EvaluateCore(context);
        if (output == null) return;

        output.HasChanges = inputs.Any(i => i?.HasChanges == true) || output.HasChanges;
        output.RemoveRange(0, output.Children.Count);
        foreach (var input in inputs.OfType<RenderNode>())
        {
            output.AddChild(input);
        }
    }

    protected abstract void EvaluateCore(NodeEvaluationContext context);
}
