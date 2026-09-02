using Beutl.Graphics.Rendering;

namespace Beutl.NodeGraph.Nodes;

internal sealed class FilterEffectInputRenderNode : RenderNode
{
    internal FilterEffectInputBinding Bind(RenderNodeContext context)
        => new(this, context);

    public override void Process(RenderNodeContext context)
        => context.PassThrough();
}
