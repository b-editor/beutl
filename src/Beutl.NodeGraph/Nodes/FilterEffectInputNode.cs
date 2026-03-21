using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public partial class FilterEffectInputNode : GraphNode
{
    public FilterEffectInputNode()
    {
        Output = AddOutput<RenderNode?>("Output");
    }

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        internal OperationWrapperRenderNode Wrapper { get; } = new();

        public override void Update(GraphCompositionContext context)
        {
            Output = Wrapper;
        }
    }
}
