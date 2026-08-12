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
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();
            var inputs = context.CollectListInputValues(node.InputPort);

            UpdateCore(context);
            var output = OutputPort;
            if (output == null) return;

            bool hasChanges = false;
            if (output.Children.Any(static child => child is not ReferencesChildRenderNode))
            {
                DetachInputReferences(output);
                hasChanges = true;
            }

            int childIndex = 0;
            foreach (RenderNode? input in inputs)
            {
                if (input is null) continue;

                ReferencesChildRenderNode reference;
                if (childIndex < output.Children.Count)
                {
                    reference = (ReferencesChildRenderNode)output.Children[childIndex];
                }
                else
                {
                    reference = new ReferencesChildRenderNode(input);
                    output.AddChild(reference);
                    hasChanges = true;
                }

                hasChanges |= reference.Update(input);
                childIndex++;
            }

            while (output.Children.Count > childIndex)
            {
                int index = output.Children.Count - 1;
                RenderNode child = output.Children[index];
                output.RemoveRange(index, 1);
                ((ReferencesChildRenderNode)child).Dispose();
                hasChanges = true;
            }

            output.HasChanges = inputs.Any(i => i?.HasChanges == true) || hasChanges || output.HasChanges;
        }

        private static void DetachInputReferences(ContainerRenderNode output)
        {
            while (output.Children.Count > 0)
            {
                int index = output.Children.Count - 1;
                RenderNode child = output.Children[index];
                output.RemoveRange(index, 1);
                if (child is ReferencesChildRenderNode reference)
                {
                    reference.Dispose();
                }
            }
        }

        protected virtual void UpdateCore(GraphCompositionContext context)
        {
        }
    }
}
