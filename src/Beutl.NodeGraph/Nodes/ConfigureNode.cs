using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public abstract partial class ConfigureNode : GraphNode
{
    public ConfigureNode()
    {
        OutputPort = AddOutput<ContainerRenderNode?>("Output");
        InputPort = AddListInput<RenderNode?>("Input");
    }

    protected OutputPort<ContainerRenderNode?> OutputPort { get; }

    protected ListInputPort<RenderNode?> InputPort { get; }

    public partial class Resource
    {
        protected sealed override void UpdateCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            var inputs = context.CollectListInputValues(node.InputPort);

            UpdateConfiguredCore(context);
            var output = OutputPort;
            if (output == null) return;

            output.HasChanges = inputs.Any(i => i?.HasChanges == true) || output.HasChanges;
            output.RemoveRange(0, output.Children.Count);
            foreach (var input in inputs.OfType<RenderNode>())
            {
                output.AddChild(input);
            }
        }

        protected virtual void UpdateConfiguredCore(GraphCompositionContext context)
        {
        }
    }
}
