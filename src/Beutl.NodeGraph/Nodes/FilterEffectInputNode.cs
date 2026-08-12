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
        internal FilterEffectInputRenderNode InputFacade { get; } = new();

        public override void Update(GraphCompositionContext context)
        {
            Output = InputFacade;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                InputFacade.Dispose();
            }
        }
    }
}

internal sealed class FilterEffectInputRenderNode : RenderNode
{
    internal FilterEffectInputBinding Bind(RenderNodeContext context)
        => new(this, context);

    public override void Process(RenderNodeContext context)
        => context.PassThrough();
}
