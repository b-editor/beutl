using Beutl.Graphics.Rendering;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes;

public abstract partial class ConfigureNode : Node
{
    public ConfigureNode()
    {
        OutputSocket = AddOutput<ContainerRenderNode?>("Output");
        InputSocket = AddListInput<RenderNode?>("Input");
    }

    protected OutputSocket<ContainerRenderNode?> OutputSocket { get; }

    protected ListInputSocket<RenderNode?> InputSocket { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            var node = GetOriginal();
            var inputs = context.CollectListInputValues(node.InputSocket);

            EvaluateCore(context);
            var output = OutputSocket;
            if (output == null) return;

            output.HasChanges = inputs.Any(i => i?.HasChanges == true) || output.HasChanges;
            output.RemoveRange(0, output.Children.Count);
            foreach (var input in inputs.OfType<RenderNode>())
            {
                output.AddChild(input);
            }
        }

        protected virtual void EvaluateCore(NodeRenderContext context)
        {
        }
    }
}
